import copy,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from check_modern_lighting import validate_result

class LightingResultTests(unittest.TestCase):
 def test_requires_both_pipeline_settings_for_all_three_types(self):
  value={'run_id':'run','cases':[dict(type=kind,pipeline_settings=flag,properties_preserved=True) for kind in ('Point','Spot','Directional') for flag in (True,False)]}
  validate_result(value,'run')
  for invalid in [dict(value,cases=value['cases'][::2]),dict(value,cases=value['cases'][:-1]+[value['cases'][0]])]:
   with self.assertRaises(ValueError):validate_result(invalid,'run')
  invalid=copy.deepcopy(value);invalid['cases'][0]['pipeline_settings']=1
  with self.assertRaises(ValueError):validate_result(invalid,'run')

if __name__=='__main__':unittest.main()
