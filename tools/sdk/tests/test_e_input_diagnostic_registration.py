"""Catalog and tampering checks; no desktop or Unity."""
from pathlib import Path
import sys,unittest,tempfile,json,time,copy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import verify_e
IDS=('motion-reconcile','motion-reconcile-negative-requery','motion-reconcile-negative-skip-intermediate','motion-reconcile-negative-release-forward','motion-reconcile-negative-deferred-time','motion-reconcile-negative-deferred-held','motion-reconcile-negative-deferred-identity','motion-reconcile-negative-cursor-lag','mouse-raw-trace','mouse-raw-trace-negative','injector-preflight','injector-preflight-negative')
class Registration(unittest.TestCase):
 def test_catalog(self):
  by={c.id:c for c in verify_e.registry()}
  selected=verify_e.selection(list(by.values()),list(IDS))
  self.assertEqual({c.id for c in selected},set(IDS))
  for name in IDS:
   c=by[name]
   self.assertEqual(c.resource,'background')
   self.assertFalse(c.dependencies)
   self.assertEqual(c.folder,'E/'+name.split('-negative')[0])
   self.assertTrue(c.sources_required)
   self.assertTrue((verify_e.ROOT/c.script).is_file())
   if '-negative' in name:self.assertTrue(c.log_contains)
  self.assertEqual(sum(by[n].nonzero for n in IDS),7)
 def test_report_tampering(self):
  by={c.id:c for c in verify_e.registry()}
  with tempfile.TemporaryDirectory() as tmp:
   base=Path(tmp);path=base/'report.json';receipt={'fixture':'digest'}
   for name in IDS:
    c=by[name];data={'status':c.statuses[0],'sources':receipt,**c.required}
    for log,text in c.log_contains.items():(base/log).write_text(text,encoding='utf-8')
    def check(value):
     path.write_text(json.dumps(value),encoding='utf-8')
     verify_e.validate_report(c,path,base,set(),time.time()-10,receipt)
    check(data)
    for key in ('status','sources',*c.required):
     bad=copy.deepcopy(data);bad[key]='tampered'
     with self.subTest(case=name,key=key),self.assertRaises(ValueError):check(bad)
    for log,text in c.log_contains.items():
     (base/log).write_text('unrelated failure',encoding='utf-8')
     with self.subTest(case=name,log=log),self.assertRaisesRegex(ValueError,'required-log-oracle'):check(data)
     (base/log).write_text(text,encoding='utf-8')
if __name__=='__main__':unittest.main()
