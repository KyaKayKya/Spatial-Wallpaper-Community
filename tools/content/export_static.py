"""Verify the current standalone SDK author sample, with P3 evidence isolation."""
import shutil
import build
def main():
 build.OUT=build.ROOT/'Validation/Content/p3/static-validation';build.REPORTS=build.OUT/'reports';build.REPORTS.mkdir(parents=True,exist_ok=True)
 build.build('author')
 report=build.ROOT/'Validation/Content/p3/reports';report.mkdir(parents=True,exist_ok=True)
 for name in ['sample-texture-pixels.json','author-resource-reuse.json']:shutil.copy2(build.REPORTS/name,report/('static-'+name))
 shutil.copy2(build.OUT/'author-source-receipt.json',report/'static-author-source-receipt.json')
if __name__=='__main__':main()
