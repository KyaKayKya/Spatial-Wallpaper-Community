from pathlib import Path
import json,re
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Validation/Content/p3'
def main():
 names=['Prototype/ProjectSettings/ProjectSettings.asset','Validation/Content/p3/platform-project/ProjectSettings/ProjectSettings.asset','Validation/Content/p3/parking-author/ProjectSettings/ProjectSettings.asset','Validation/Content/p3/static-validation/author-project/ProjectSettings/ProjectSettings.asset'];cases=[]
 for name in names:
  p=ROOT/name;match=re.search(r'm_ActiveColorSpace: (\d+)',p.read_text(encoding='utf-8-sig'));value=int(match[1]);cases.append({'path':name,'active_color_space':value,'passed':value==1})
 report={'passed':all(c['passed'] for c in cases),'cases':cases};(OUT/'reports/color-baseline.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));raise SystemExit(0 if report['passed'] else 1)
if __name__=='__main__':main()
