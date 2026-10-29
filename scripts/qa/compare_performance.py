"""Compare complete seven-scene single-core CPU samples with the 2.0.9 control."""
import argparse
import json
import math
from pathlib import Path


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('baseline',type=Path);p.add_argument('candidate',type=Path)
    p.add_argument('-o','--output',type=Path,required=True)
    args=p.parse_args()
    baseline=json.loads(args.baseline.read_text(encoding='utf-8'))
    candidate=json.loads(args.candidate.read_text(encoding='utf-8'))
    expected={('Oled','claude'),('Cream','claude'),('OledDay','claude'),('ChatGpt','chatgpt'),('DeepSeek','deepseek'),('DeepSeek','deepseek-rice'),('Gemini','gemini')}
    def index(doc):
        rows=doc['Samples']; result={(r['Theme'],r['Character']):r for r in rows}
        if len(rows)!=7 or set(result)!=expected: raise ValueError('Expected all seven unique theme/character samples.')
        return result
    before,after=index(baseline),index(candidate)
    if baseline['WarmupSeconds']!=candidate['WarmupSeconds'] or baseline['SampleSeconds']!=candidate['SampleSeconds'] or baseline['RenderTier']!=candidate['RenderTier']:
        raise ValueError('Sampling protocol/render tier differs from the control.')
    rows=[]
    for key in sorted(expected):
        b,c=before[key],after[key]
        bs,cs=b.get('Scene'),c.get('Scene')
        if not bs or not cs: raise ValueError('Scene metadata is required; use the current native-EXE measurement harness.')
        if bs.get('Host')!='QuickMenu' or cs.get('Host')!='QuickMenu': raise ValueError('Use the production native EXE host on both sides.')
        for field in ('WindowDpi','CompanionDpi','WindowWidth','WindowHeight','CompanionWidth','CompanionHeight'):
            if not math.isclose(bs[field],cs[field],rel_tol=0,abs_tol=.01): raise ValueError(f'{key}: mismatched {field}.')
        if not all(s['WindowVisible'] and s['CompanionVisible'] for s in (bs,cs)) or bs['WindowActive']!=cs['WindowActive']:
            raise ValueError(f'{key}: mismatched visibility/activation.')
        delta=c['ShownSingleCorePercent']-b['ShownSingleCorePercent']
        actions=c.get('SampleIdleClips',0)
        passed=math.isfinite(delta) and delta<=1 and actions>=1
        rows.append(dict(Theme=key[0],Character=key[1],Baseline=b['ShownSingleCorePercent'],Candidate=c['ShownSingleCorePercent'],Delta=round(delta,3),Actions=actions,Hidden=c['HiddenSingleCorePercent'],SceneMatched=True,Passed=passed))
    report=dict(Baseline=str(args.baseline),Candidate=str(args.candidate),Samples=rows,AllChecksPassed=all(r['Passed'] for r in rows))
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
    return 0 if report['AllChecksPassed'] else 1


if __name__=='__main__':raise SystemExit(main())
