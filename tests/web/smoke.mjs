// Plays battles in the built phone version (dist/web) without a browser: Node runs its WebAssembly
// engine, and each battle goes through the same calls the page makes. Every encounter is tried with
// every timing mode, with random choices taken only from what the page would offer, and halfway
// through each battle its log is resumed and checked to rebuild exactly the same battle.
//
//   scripts/build-web.sh && node tests/web/smoke.mjs [battles per encounter and timing, default 3]
import { dotnet } from '../../dist/web/engine/dotnet.js';

const runtime = await dotnet.create();
const api = (await runtime.getAssemblyExports('Battle.Web.dll')).Battle.Web.Api;
const call = (name, ...args) => {
  const r = JSON.parse(api[name](...args));
  if (r.error) throw new Error(`${name}(${args.join(', ')}): ${r.error}\n${r.detail ?? ''}`);
  return r;
};

let seed = 20261018;
const rand = (n) => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
const per = Number(process.argv[2] ?? 3);
const boot = call('Boot');
const timings = [...boot.skills.map((s) => s.id), 'ask'];
const tally = { battles: 0, inputs: 0, enemyActions: 0, resumes: 0 };
const started = performance.now();

for (const { id } of boot.encounters) {
  for (const timing of timings) {
    for (let n = 0; n < per; n++) {
      let view = call('New', id, String(100 + n), timing).view;
      const resumeAt = 4 + rand(40);
      for (let steps = 0; view.outcome === 'ongoing'; steps++) {
        if (steps > 5000) throw new Error(`${id} (${timing}, seed ${100 + n}) never ended`);
        let r;
        if (view.pending) r = call('Grade', ['fumble', 'good', 'perfect'][rand(3)]);
        else if (view.walk) {
          const choices = [...view.walk.map((w) => w.i), ...view.groups.flatMap((g) => (g.kind === 'flee' && rand(10) ? [] : g.options.map((o) => o.i)))];
          r = call('Act', view.v, rand(6) === 0 || choices.length === 0 ? view.end : choices[rand(choices.length)]);
          tally.inputs++;
        } else {
          r = call('Step');
          tally.enemyActions++;
        }
        view = r.view;
        if (steps === resumeAt && view.outcome === 'ongoing' && !view.pending) {
          const again = call('Resume', api.Log()).view;
          if (JSON.stringify(again) !== JSON.stringify(view)) throw new Error(`resuming ${id} (${timing}, seed ${100 + n}) changed the battle`);
          tally.resumes++;
        }
      }
      tally.battles++;
    }
  }
}
console.log(`${tally.battles} battles, ${tally.inputs} party inputs, ${tally.enemyActions} enemy actions, ${tally.resumes} resumes checked, in ${((performance.now() - started) / 1000).toFixed(1)}s`);
