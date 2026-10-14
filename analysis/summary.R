# Summarises simulated battles from the logs the simulator writes. Base R only: nothing to install.
#
#   Rscript analysis/summary.R logs                 every log in the folder
#   Rscript analysis/summary.R logs goblin-camp     only files whose names contain "goblin-camp"
#
# Make some logs first, for example:
#   dotnet run --project src/Battle.Sim -c Release -- run goblin-camp --battles 20 --out logs
#   dotnet run --project src/Battle.Sim -c Release -- balance --battles 200 --out logs
#
# It reads whichever of these it finds in the folder:
#   <encounter>-summary.csv   one row per battle               (battle-sim run)
#   balance.csv               win rates by encounter and skill  (battle-sim balance)
#   *.events.csv              every event of every battle       (battle-sim run, battle-play)
# prints a few tables, and saves charts to <folder>/summary.pdf.
#
# The event log columns are described in content/README.md and README.md ("Logs").

args <- commandArgs(trailingOnly = TRUE)
folder <- if (length(args) >= 1) args[1] else "logs"
pattern <- if (length(args) >= 2) args[2] else ""
if (!dir.exists(folder)) stop("no folder called '", folder, "': run the simulator first (see the top of this file)")

files <- function(suffix) {
  found <- list.files(folder, pattern = paste0(suffix, "$"), full.names = TRUE)
  found[grepl(pattern, basename(found), fixed = TRUE)]
}
rule <- function(title) cat("\n", title, "\n", strrep("-", nchar(title)), "\n", sep = "")
pct <- function(x) sprintf("%.0f%%", 100 * x)

# ---------------------------------------------------------------------------
# 1. Battles: one row each (battle-sim run writes <encounter>-summary.csv)
# ---------------------------------------------------------------------------

summaries <- files("-summary.csv")
battles <- NULL
if (length(summaries) > 0) {
  battles <- do.call(rbind, lapply(summaries, read.csv, stringsAsFactors = FALSE))
  rule(sprintf("Battles (%d, from %d summary file%s)", nrow(battles), length(summaries), if (length(summaries) == 1) "" else "s"))
  by_encounter <- split(battles, list(battles$encounter, battles$skill), drop = TRUE)
  table1 <- do.call(rbind, lapply(by_encounter, function(b) data.frame(
    encounter = b$encounter[1], skill = b$skill[1], battles = nrow(b),
    won = pct(mean(b$outcome == "won")), lost = pct(mean(b$outcome == "lost")), drawn = pct(mean(b$outcome == "draw")),
    rounds = round(mean(b$rounds), 1), hero_hp = round(mean(b$hero_hp), 1), loper_hp = round(mean(b$loper_hp), 1),
    fled = round(mean(b$enemies_fled), 2))))
  rownames(table1) <- NULL
  print(table1, row.names = FALSE)
}

# ---------------------------------------------------------------------------
# 2. Balance: win rates by encounter and timing skill (battle-sim balance)
# ---------------------------------------------------------------------------

balance <- NULL
balance_file <- file.path(folder, "balance.csv")
if (file.exists(balance_file)) {
  balance <- read.csv(balance_file, stringsAsFactors = FALSE)
  balance <- balance[grepl(pattern, balance$encounter, fixed = TRUE), ]
  rule("Balance: how often the party wins")
  wide <- reshape(balance[, c("encounter", "skill", "win_rate")], idvar = "encounter", timevar = "skill", direction = "wide")
  names(wide) <- sub("win_rate.", "", names(wide), fixed = TRUE)
  for (col in names(wide)[-1]) wide[[col]] <- pct(wide[[col]])
  print(wide, row.names = FALSE)
  cat(sprintf("(%d battles per cell; party %s vs enemies %s; rules %s)\n",
              balance$battles[1], balance$party[1], balance$enemies[1], balance$rules[1]))
}

# ---------------------------------------------------------------------------
# 3. Events: what actually happened, move by move
# ---------------------------------------------------------------------------

event_files <- files(".events.csv")
events <- NULL
if (length(event_files) > 0) {
  read_events <- function(path) {
    e <- read.csv(path, stringsAsFactors = FALSE, colClasses = "character")
    e$battle <- sub(".events.csv", "", basename(path), fixed = TRUE)
    e
  }
  events <- do.call(rbind, lapply(event_files, read_events))
  for (col in c("round", "seq", "amount", "hp_after")) events[[col]] <- suppressWarnings(as.integer(events[[col]]))
  party_labels <- c("hero", "loper")
  events$side <- ifelse(events$actor %in% party_labels, "party", ifelse(events$actor == "", "", "enemies"))

  # Damage by move: how often it lands, how hard, and how often it finishes someone off.
  damage <- events[events$event == "damage" & events$move != "", ]
  deaths <- events[events$event %in% c("death", "knocked_out"), ]
  killing_move <- vapply(seq_len(nrow(deaths)), function(i) {
    d <- deaths[i, ]
    before <- damage[damage$battle == d$battle & damage$target == d$target & damage$seq < d$seq, ]
    if (nrow(before) == 0) "" else before$move[which.max(before$seq)]
  }, character(1))

  rule(sprintf("Damage by move (%d battles, %d hits)", length(event_files), nrow(damage)))
  moves <- sort(unique(damage$move))
  table3 <- do.call(rbind, lapply(moves, function(m) {
    d <- damage[damage$move == m, ]
    data.frame(move = m, side = d$side[1], hits = nrow(d), total = sum(d$amount), per_hit = round(mean(d$amount), 2),
               zero = pct(mean(d$amount == 0)), kills = sum(killing_move == m))
  }))
  table3 <- table3[order(table3$side, -table3$total), ]
  print(table3, row.names = FALSE)

  # Timing grades: the party's action commands, and its guards against enemy attacks.
  graded <- damage[damage$timing != "", ]
  if (nrow(graded) > 0) {
    rule("Timing grades")
    graded$what <- ifelse(graded$side == "party", "attack (party command)", "guard (against enemies)")
    print(table(graded$what, factor(graded$timing, levels = c("fumble", "good", "perfect"))))
  }

  # Statuses: who gets what, from which move.
  statuses <- events[events$event == "status" & events$detail != "", ]
  if (nrow(statuses) > 0) {
    rule("Statuses applied (by move)")
    print(table(ifelse(statuses$move == "", "(no move)", statuses$move), statuses$detail))
  }

  # Morale: goblins that broke and goblins that got away.
  rule("Nerve")
  cat(sprintf("goblins broke and ran in %d of %d battles; %d got away\n",
              length(unique(events$battle[events$event == "flee_start"])), length(event_files), sum(events$event == "fled")))

  # Pace: damage per round to each side.
  rule("Damage per round, summed over battles")
  damage$taken_by <- ifelse(damage$target %in% party_labels, "party took", "enemies took")
  print(tapply(damage$amount, list(damage$round, damage$taken_by), sum, default = 0))
}

if (is.null(battles) && is.null(balance) && is.null(events)) {
  stop("found no logs in '", folder, "'", if (pattern != "") paste0(" matching '", pattern, "'"), ": run the simulator first")
}

# ---------------------------------------------------------------------------
# Charts
# ---------------------------------------------------------------------------

pdf_path <- file.path(folder, "summary.pdf")
pdf(pdf_path, width = 8, height = 5)
if (!is.null(balance)) {
  m <- tapply(balance$win_rate, list(balance$skill, balance$encounter), mean)
  m <- m[intersect(c("novice", "average", "expert"), rownames(m)), , drop = FALSE]
  barplot(100 * m, beside = TRUE, legend.text = rownames(m), ylim = c(0, 125), las = 1,
          ylab = "party wins (%)", main = "Win rate by encounter and timing skill",
          args.legend = list(x = "top", bty = "n", horiz = TRUE))
}
if (!is.null(battles)) {
  hist(battles$rounds, breaks = seq(0, max(battles$rounds) + 1, 1), col = "grey80", border = "white", las = 1,
       xlab = "rounds", main = "How long battles last")
}
if (!is.null(events) && nrow(damage) > 0) {
  totals <- sort(tapply(damage$amount, damage$move, sum))
  sides <- tapply(damage$side, damage$move, function(x) x[1])[names(totals)]
  op <- par(mar = c(4, 8, 3, 1))
  barplot(totals, horiz = TRUE, las = 1, col = ifelse(sides == "party", "steelblue", "firebrick"),
          xlab = "total damage", main = "Damage by move (blue: party, red: enemies)")
  par(op)
}
invisible(dev.off())
cat("\ncharts saved to", pdf_path, "\n")
