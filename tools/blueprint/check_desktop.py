"""Reuse all eight existing desktop lifecycle cases, then two saved blueprint works."""
from pathlib import Path
import subprocess,sys,json
r=Path(__file__).resolve().parents[2];data=r/'Validation/Blueprint/local-data';state=json.loads((data/'index.json').read_text(encoding='utf-8-sig'))
works=list(state['works'].items())[-2:];assert len(works)==2,'Run actual editor-check first'
command=[sys.executable,str(r/'tools/desktop/run.py'),'--blueprint-data-root',str(data)]
for work,record in works:command+=['--additional-artwork','blueprint-'+work+'-'+record['latest']]
subprocess.run(command,check=True)
