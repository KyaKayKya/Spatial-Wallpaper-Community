"""Read numeric Player traces; tests data flow, not physical eye-position accuracy."""
import argparse
import csv
import json
from pathlib import Path

import numpy as np


def analyze(path,candidate=None):
    with path.open(encoding='utf-8-sig',newline='') as stream: rows=list(csv.DictReader(stream))
    fresh=[r for r in rows if r['fresh']=='True' and r['compatible']=='True' and r['mapped_valid']=='True']
    report={'rows':len(rows),'fresh_compatible_rows':len(fresh),'physical_accuracy_verified':False}
    if not fresh: return report
    def values(names): return np.array([[float(r[n]) for n in names] for r in fresh])
    raw=values(['qx','qy','qz']); mapped=values(['mapped_x','mapped_y','mapped_z'])
    filtered=values(['filtered_x','filtered_y','filtered_z'])
    projected=values(['projection_x','projection_y','projection_z'])
    centers=np.column_stack((values(['view_center_x','view_center_y']),np.zeros(len(fresh))))
    report.update(raw_span=np.ptp(raw,axis=0).tolist(),mapped_span=np.ptp(mapped,axis=0).tolist(),
                  projection_span=np.ptp(projected,axis=0).tolist(),
                  mapped_median=np.median(mapped,axis=0).tolist(),
                  sample_age_ms_median_p95=(np.percentile(values(['sample_age_us']),[50,95])/1000).tolist(),
                  projection_vs_filter_minus_view_max=float(np.max(np.abs(projected-(filtered-centers)))),
                  corner_error_px_p95_max=np.percentile(values(['corner_error_px']),[95,100]).tolist())
    if candidate:
        mapping=json.loads(candidate.read_text(encoding='utf-8'))['mapping']
        q=np.array(mapping['q_xyzw']);v=raw-np.array(mapping['q0'])
        expected=np.array(mapping['e0'])+mapping['scale']*(v+2*np.cross(q[:3],np.cross(q[:3],v)+q[3]*v))
        report['mapped_vs_candidate_max']=float(np.max(np.abs(expected-mapped)))
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('csv',type=Path);parser.add_argument('--candidate',type=Path)
    args=parser.parse_args()
    print(json.dumps(analyze(args.csv,args.candidate),indent=2))
