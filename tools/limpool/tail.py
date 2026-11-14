from pathlib import Path
import sys
r=Path(__file__).resolve().parents[2]
p=r/sys.argv[1]
print(p.read_text(encoding='utf-8',errors='replace')[-int(sys.argv[2] if len(sys.argv)>2 else 3500):])
