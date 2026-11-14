"""Check actual ETW sync policy against the original desktop's SyncInterval=0."""
from pathlib import Path
import argparse,csv,json
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('report',type=Path);a=p.parse_args();report=json.loads(a.report.read_text(encoding='utf-8'));rows=list(csv.DictReader((a.report.parent/'presents.csv').open(encoding='utf-8-sig')))
 for case in report['cases']:
  selected=[r for r in rows if int(r['ProcessID'])==case['child_pid'] and case['start_qpc_ms']<=float(r['CPUStartQPCTimeInMs'])<case['end_qpc_ms']]
  values={int(r['SyncInterval']) for r in selected};assert values=={0},(case['artwork'],'native cadence differs from original desktop',values)
 print('PASS actual native ETW SyncInterval=0',len(report['cases']),'work(s)')
if __name__=='__main__':main()
