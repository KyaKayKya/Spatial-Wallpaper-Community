import copy
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
from input_continuity_evidence import validate_mixed

class MixedEvidenceChecks(unittest.TestCase):
    def fixture(self):
        proof=dict(mode='development-short-only',requested_seconds=1,elapsed_ms=1400,cycles=1,author_edges=14,source_edges=14,dropped_edges=0,owed=0,epoch=3,mixed_motion=True,moves_requested=20,hook_moves=20,move_commits=10)
        names=['Space','Left','Right','Up','Down','Primary','Secondary'];bits=[1,8,16,32,64,2,128]
        rows=[]
        for i in range(14):
            steps=min(i+1,10)
            rows.append(dict(edge=i+1,revision=i+1,source_edges=i+1,cycle=0,step=i,obligations=0 if i%2 else bits[i//2],elapsed_ms=(i+1)*100,epoch=3,ack=names[i//2]+('-up' if i%2 else '-down'),moves_requested=steps*2,hook_moves=steps*2,move_commits=steps))
        return proof,rows
    def test_complete_mixed_proof(self):
        proof,rows=self.fixture();validate_mixed(proof,rows,1)
    def test_missing_hook_cannot_be_hidden_by_later_total(self):
        proof,rows=self.fixture();rows[4]['hook_moves']-=1
        with self.assertRaisesRegex(ValueError,'mixed-hook-edge'):validate_mixed(proof,rows,1)
    def test_missing_commit_is_rejected(self):
        proof,rows=self.fixture();rows[4]['move_commits']-=1
        with self.assertRaisesRegex(ValueError,'mixed-commit-edge'):validate_mixed(proof,rows,1)
    def test_forged_injection_count(self):
        proof,rows=self.fixture();proof['moves_requested']=19
        with self.assertRaisesRegex(ValueError,'mixed-request-count'):validate_mixed(proof,rows,1)
    def test_epoch_recovery_cannot_hide_loss(self):
        proof,rows=self.fixture();rows[4]['epoch']=4
        with self.assertRaisesRegex(ValueError,'epoch-changed'):validate_mixed(proof,rows,1)
    def test_false_mode_is_rejected(self):
        proof,rows=self.fixture();proof['mixed_motion']=False
        with self.assertRaisesRegex(ValueError,'mixed-mode'):validate_mixed(proof,rows,1)
    def test_last_row_must_bind_totals(self):
        proof,rows=self.fixture();proof['hook_moves']=21
        with self.assertRaisesRegex(ValueError,'mixed-final-count'):validate_mixed(proof,rows,1)

if __name__=='__main__':unittest.main()
