import csv
import tempfile
import time
import unittest
from pathlib import Path
import run


class TrackingReportTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path=Path(self.folder.name)
        (self.path/'complete.txt').write_text('accepted=3; rejected=0; state=TRACKING\n')
        with (self.path/'frames.csv').open('w',newline='') as stream:
            writer=csv.DictWriter(stream,fieldnames=['fresh','cornerErrorPx','eyeX','eyeY','eyeZ'])
            writer.writeheader()
            for x in ['0','.01']:writer.writerow(dict(fresh='True',cornerErrorPx='.001',eyeX=x,eyeY='0',eyeZ='-.6'))

    def test_accepts_fresh_successful_tracking(self):
        run.verify_tracking(self.path,time.time()-2)

    def test_rejects_zero_accepted_even_when_player_exits_successfully(self):
        (self.path/'complete.txt').write_text('accepted=0; rejected=0; state=OFF\n')
        with self.assertRaises(AssertionError):run.verify_tracking(self.path,time.time()-2)

    def test_rejects_stale_reports(self):
        with self.assertRaises(AssertionError):run.verify_tracking(self.path,time.time()+10)

    def test_rejects_incorrect_projection(self):
        text=(self.path/'frames.csv').read_text().replace('.001','1.0')
        (self.path/'frames.csv').write_text(text)
        with self.assertRaises(AssertionError):run.verify_tracking(self.path,time.time()-2)


if __name__=='__main__':unittest.main()
