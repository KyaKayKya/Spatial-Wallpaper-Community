"""Historical review records stay separate from fresh release execution."""
import copy,hashlib,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import historical_failure_closure as audit
from unittest.mock import patch

class HistoricalClosure(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  self.ref={}
  for name,text in [('old/failure.log','actual failure'),('old/red.log','targeted reproduction fails'),('old/green.log','actual runtime and cleanup pass'),('review.md','independent accepted limited fix')]:
   path=self.root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(text)
   self.ref[name]={'path':name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'scope':text}
  self.incident={'id':'input-example','category':'input-timeout','status':'closed','historical_runs':['old'],'scope':'only the proven defect','closure':{'root_cause':'the actual causal explanation','fix_commit':'1'*40,'failure_evidence':[self.ref['old/failure.log']], 'red':self.ref['old/red.log'],'green':self.ref['old/green.log'],'real':self.ref['old/green.log'],'review':{'decision':'accepted','implementer':'implementation','reviewer':'independent review','reference':self.ref['review.md']},'remaining':'other unlocalized timeouts remain open'}}
  self.catalog={'schema':1,'required_categories':['input-timeout'],'required_incidents':['input-example'],'incidents':[self.incident]}
  self.catalog['required_categories']=sorted(audit.CATEGORIES)
  for name,category in [('author-example','author-revision-loss'),('host-example','host-exit-timeout')]:
   other=copy.deepcopy(self.incident);other.update(id=name,category=category);self.catalog['incidents'].append(other);self.catalog['required_incidents'].append(name)
  self.commit=patch.object(audit,'commit_exists',return_value=True);self.commit.start();self.addCleanup(self.commit.stop)
 def save(self):
  path=self.root/audit.CATALOG;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(self.catalog))
  return {audit.CATALOG:audit.digest(path.read_bytes())}
 def check(self):return audit.evaluate_current(self.root,self.save())
 def test_reviewed_old_raw_evidence_needs_no_new_execution_wrapper(self):
  self.assertEqual(self.check()['status'],'passed-historical-closure')
 def test_unrelated_current_source_does_not_invalidate_historical_review(self):
  receipt=self.save();receipt['tools/sdk/unrelated.py']='changed'
  self.assertEqual(audit.evaluate_current(self.root,receipt)['status'],'passed-historical-closure')
 def test_unknown_root_cause_stays_open_despite_later_pass(self):
  self.incident['status']='open';self.incident['open_reason']='unlocalized'
  result=self.check();self.assertEqual(result['status'],'blocked');self.assertEqual(result['open_incidents'],['input-example'])
 def test_closed_record_requires_every_substantive_part(self):
  original=copy.deepcopy(self.incident['closure'])
  for name in ['root_cause','fix_commit','failure_evidence','red','green','real','review']:
   with self.subTest(name=name):
    self.incident['closure']=copy.deepcopy(original);self.incident['closure'].pop(name)
    self.assertEqual(self.check()['status'],'blocked')
 def test_changed_or_missing_evidence_cannot_close(self):
  for name in ['old/failure.log','old/red.log','old/green.log','review.md']:
   path=self.root/name;old=path.read_bytes();path.write_bytes(b'changed')
   self.assertEqual(self.check()['status'],'blocked');path.write_bytes(old)
  (self.root/'old/red.log').unlink();self.assertEqual(self.check()['status'],'blocked')
 def test_failed_or_nonindependent_review_cannot_close(self):
  for changed in [{'decision':'rejected'},{'reviewer':'implementation'}]:
   self.incident['closure']['review'].update(changed);self.assertEqual(self.check()['status'],'blocked')
 def test_removing_required_incident_or_reusing_id_cannot_pass(self):
  self.catalog['incidents']=[];self.assertEqual(self.check()['status'],'blocked')
  self.catalog['incidents']=[self.incident,self.incident];self.assertEqual(self.check()['status'],'blocked')
 def test_stale_catalog_receipt_and_escape_are_refused(self):
  self.save()
  self.assertEqual(audit.evaluate_current(self.root,{audit.CATALOG:'0'*64})['status'],'blocked')
  self.incident['closure']['red']['path']='../outside';self.assertEqual(self.check()['status'],'blocked')
 def test_unknown_fix_commit_and_identical_red_green_cannot_close(self):
  with patch.object(audit,'commit_exists',return_value=False):self.assertEqual(self.check()['status'],'blocked')
  self.incident['closure']['red']=self.ref['old/green.log'];self.assertEqual(self.check()['status'],'blocked')
 def test_missing_empty_or_unlinked_original_runs_cannot_close(self):
  for runs in [None,[],[''],['not-the-original-folder']]:
   with self.subTest(runs=runs):
    self.incident['historical_runs']=runs;self.assertEqual(self.check()['status'],'blocked')
 def test_supporting_evidence_is_checked_when_supplied(self):
  self.incident['closure']['supporting_evidence']=[copy.deepcopy(self.ref['old/green.log'])]
  self.assertEqual(self.check()['status'],'passed-historical-closure')
  self.incident['closure']['supporting_evidence'][0]['sha256']='0'*64
  self.assertEqual(self.check()['status'],'blocked')

if __name__=='__main__':unittest.main()
