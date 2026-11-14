"""Explicit executable E catalog; statuses are runner contracts, not E verdicts."""

CONTINUITY_KEYS = ('Space','A','Left','Right','Up','Down','Primary','Secondary')
INTERACTION_REQUIRED = ('worker','continuity-author','continuity-preflight','continuity-short',
                        'continuity-drop-release',*("continuity-kill-"+key.lower() for key in CONTINUITY_KEYS),
                        'continuity-1800')


def cases(Case):
    rows = []
    def add(name, script, status='', folder='', args=(), deps=(), **kw):
        rows.append(Case(name, script, tuple(args), tuple(deps), folder,
                         (status,) if status else (), **kw))
    def sdk(name, status='', folder='', args=(), **kw):
        add(name.replace('_', '-'), 'tools/sdk/'+name+'.py', status, folder, args, **kw)
    def test(name, status, folder=None, args=(), id=None, **kw):
        add(id or name.replace('_', '-'), 'Supervisor/Tests/run_'+name+'.py', status,
            'E/'+(folder or name.replace('_', '-')), args, **kw)

    sdk('generate_rules', args=('--check',))
    sdk('compile_package', resource='unity',timeout=900,report_file='reports/stage-B-unity.json',
        source_field='source_hashes',required={'engine':'2022.3.62f3','rejected':True})
    sdk('check_execution',output_files=('B/checks/ExecutionChecks.exe','B/checks/execution.spatial','B/checks/instructions.spatial'))
    sdk('check_capabilities')  # Exit-only runner; wrapper binds log and source receipt.
    sdk('check_capability_rules', 'passed-static-capability-rule-closure', 'G/capability-rules')
    for name, status, folder in [
        ('check_capability_client', 'E2-client-mailbox-core-only', 'capability-client'),
        ('check_input_client', 'partial-real-pipe-shared-input-client', 'input-client'),
        ('check_input_snapshot', 'partial-input-snapshot-only', 'input-snapshot'),
        ('check_audio_snapshot', 'partial-audio-snapshot-only', 'audio-snapshot')]:
        sdk(name, status, 'E/'+folder)
    # The unit suite includes rejection of previous normalized VM identities.
    add('sdk-tests', 'tools/sdk/run_e_unit_tests.py')
    for name, status in [('boolean_joins','partial-real-boolean-join-regression'),
                         ('reference_branches','real-reference-branch-regression'),
                         ('string_equality','real-string-equality-regression')]:
        sdk('check_'+name, status, 'E/'+name.replace('_','-'), resource='unity', timeout=1200)
    add('worker', 'tools/sdk/build_worker.py', 'production-Worker-built-not-acceptance', 'D/worker',
        resource='unity', timeout=2400, role='worker')
    for name, status, args in [
        ('storage','E-author-CSharp-converted-and-ordinary-Play',('--play',)),
        ('input','partial-input-CSharp-export-only',('--runtime-fixture',)),
        ('audio','partial-audio-CSharp-Play-only',('--stable-zero',)),
        ('llm','partial-LLM-CSharp-Play-only',())]:
        add(name+'-author','tools/sdk/build_'+name+'_author.py',status,'E/'+name+'-author',args,
            resource='unity',timeout=1500,role='author',
            required={'stableZeroPackage':True,'negativeStabilityVerdictPackage':False} if name == 'audio' else {})
    add('continuity-author','tools/sdk/build_input_author.py','partial-input-CSharp-export-only','E/input-author',
        ('--continuity-fixture',),resource='unity',timeout=1500,role='author',
        required={'author.fixture':'input-continuity-14-v1','author.capabilities':['input','private-storage']})
    add('matrix-authors','tools/sdk/build_storage_matrix.py',folder='E/storage-matrix-author',
        resource='unity',timeout=1200,role='matrix-authors')
    add('audio-negative-author','tools/sdk/build_audio_author.py','partial-audio-CSharp-Play-only','E/audio-author',
        ('--stable-zero','--negative-stability-verdict'),resource='unity',timeout=1500,role='author',
        required={'stableZeroPackage':True,'negativeStabilityVerdictPackage':True})
    test('validator','E2-restricted-Validator-only')
    test('launch_admission','E2-production-launch-transaction-only','launch-admission',
         ('--worker','{worker}'),deps=('worker',))
    test('capability_endpoint','E2-endpoint-core-only')
    test('capability_pipe','E2-real-LPAC-capability-pipe-fixture-only')
    test('worker_stop','passed-worker-stop-control',required={'expected_negative':False},
         result_cases=('normal','hang','nonzero','wrong','wrong-instance','wrong-generation'))
    test('worker_stop','expected-invalid-stop-identity-rejected',args=('--negative-identity',),
         id='worker-stop-identity-negative',required={'expected_negative':True})
    for mode in ('final-observation','exited-kill','unpublished-exit','ready-after-stop'):
        add('stop-'+mode,'Supervisor/Tests/run_runtime.py',args=('--negative-'+mode,))
    test('capability_endpoint','E2-endpoint-core-only',args=('--negative-close-race',),
         id='capability-stop-close-race',required={'expected_negative':True})
    test('storage','E1-storage-core-only')
    test('storage_actor','E2-storage-actor-core-only')
    for name, status, folder in [('storage','E-CSharp-Worker-storage-only','storage-worker'),
                                 ('input','E-CSharp-Worker-input-fixture-only','input-worker'),
                                 ('llm','partial-LLM-CSharp-LPAC-only','llm-worker')]:
        test(name+'_author',status,folder,('--worker','{worker}','--author','{'+name+'-author}'),
             deps=('worker',name+'-author'),id=name+'-worker')
    test('storage_matrix','passed','storage-matrix',('--worker','{worker}','--authors','{matrix-authors}'),
         deps=('worker','matrix-authors'),id='storage-matrix',timeout=600,required={'exit_code':0},
         result_cases=('two-live-LPAC-workers-CAS','cross-build-after-broker-restart','other-installation-empty',
                       'preview-empty','same-id-reinstalled-empty','preview-cleared','new-id-reinstalled-empty',
                       'unrelated-installation-nonempty-preserved','read-Io-rejected-after-storage-recovered',
                       'log-write-failure-still-cleans-worker','result-marker-conflict-retries-without-retrying-winner'))
    add('storage-evidence-negatives','Supervisor/Tests/check_storage_matrix_evidence.py','passed',
        'E/storage-matrix-evidence-negative',('--worker','{worker}','--authors','{matrix-authors}'),
        ('worker','matrix-authors'),result_key='cases',result_cases=('missing-role','extra-role','actual-metadata','wrong-role',
            'package-bytes','build-id','duplicate-build','author-source','compiler-source','missing-result','duplicate-result','empty-results',
            'removed-author-dependency','removed-worker-dependency'))
    for name, status in [
        ('input_gate','partial-input-gate-only'),('input_broker','partial-trusted-input-publisher'),
        ('input_recovery','partial-controlled-input-recovery'),
        ('keyboard_heartbeat','passed-keyboard-invalidation-state-only'),
        ('input_author_fixture','partial-author-fixture-frame-semantics'),
        ('hit_classification','partial-owned-listview-classification'),
        ('hit_trace','passed-telemetry-boundary-and-interactive-mock-checks')]:
        test(name,status,'desktop-hit-trace' if name=='hit_trace' else None)
    test('motion_reconcile','passed-state-replay-only',
         required={'negative':None,'compile_exit':0,'result_exit':0,'pipe_short_tap':True,'pipe_overflow_cleanup':True,'pipe_accepted_down_overflow_cleanup':True},
         log_contains={'result.log':'PASS pipe-overflow-final-author-state-cleared'})
    for mode,oracle in (
        ('requery','return-to-old-point-cannot-restart-heartbeat-before-raw-move'),
        ('skip-intermediate','intermediate-icon-and-return-cannot-revive'),
        ('release-forward','owned-up-is-consumed-without-native-forward'),
        ('deferred-time','queued-short-pair-preserves-original-time-and-both-edges'),
        ('deferred-held','middle-or-equal-move-cannot-use-future-key-state'),
        ('deferred-identity','invalid-queued-path-revokes-and-discards-without-publishing'),
        ('cursor-lag','lagging-global-cursor-cannot-start-old-point-heartbeat'),
        ('pipe-held-clear','pipe-accepted-down-overflow-final-author-state-cleared')):
        test('motion_reconcile','failed',args=('--negative',mode),id='motion-reconcile-negative-'+mode,
             nonzero=True,required={'negative':mode,'compile_exit':0,'result_exit':1,**({'accepted_down_overflow_preconditions':True} if mode=='pipe-held-clear' else {})},
             log_contains={'result.log':'System.Exception: '+oracle})
    for negative in (False,True):
        test('mouse_raw_trace','expected-raw-flags-loss-rejected' if negative else 'passed-memory-controls-and-client-compile-only',
             args=('--repository','.')+(('--negative-omit-flags',) if negative else ()),
             id='mouse-raw-trace'+('-negative' if negative else ''),
             required={'negative_omit_flags':negative,'checks_exit':1 if negative else 0},
             log_contains={'checks.log':'System.Exception: raw-native-fields-exact-including-high-extra-bit'} if negative else {})
        test('injector_preflight','expected-diagnostic-exception-rejected' if negative else 'passed-controlled-diagnostic',
             args=('--negative-diagnostic-throws',) if negative else (),
             id='injector-preflight'+('-negative' if negative else ''),
             required={'compile_exit':0,'result_exit':1 if negative else 0,
                       **({'mutation':'diagnostic-exception-escapes-rejection'} if negative else {})},
             log_contains={'result.log':'System.IO.IOException: controlled-write-failure' if negative else 'PASS write-failure-keeps-rejection'})
    for mode in ('preview','desktop'):
        test('input_channel','partial-input-channel-only',args=('--desktop',) if mode=='desktop' else (),id='input-channel-'+mode)
    for mode in ('owner','fence','heartbeat'):
        test('input_thread','partial-owned-input-thread-lifecycle',args=() if mode=='owner' else ('--'+mode,),id='input-thread-'+mode)
    for mode in ('before','connecting','cleanup'):
        test('receiver_lifetime','partial-real-window-controlled-connection',args=(mode,),id='receiver-'+mode,resource='desktop')
    for name,args,status in [
        ('fault',(),'partial-bounded-desktop-hit-client'),
        ('cleanup',('--hook-cleanup',),'partial-native-hook-cleanup'),
        ('acquisition',('--acquisition-state',),'partial-acquisition-state-negatives'),
        ('real',('--real',),'partial-real-explorer-hit'),
        ('adapter',('--desktop-input',),'partial-native-desktop-input'),
        ('no-adapter',('--desktop-input','--negative-no-adapter'),'partial-desktop-input-missing-adapter-negative'),
        ('delayed',('--desktop-input','--delayed-confirmation'),'partial-native-desktop-input')]:
        test('desktop_hit',status,'desktop-hit-client',args,id='hit-'+name,resource='desktop' if name in ('real','adapter','no-adapter','delayed') else 'background')
    for negative in (False,True):
        test('preview_input','partial-native-preview-input-only','native-preview-input',
             ('--negative-no-adapter',) if negative else (),id='preview-adapter-'+str(negative),resource='desktop')
    for name,args,status in [
        ('base',(),'partial-native-ordinary-Play-input'),
        ('no-adapter',('--negative-no-adapter',),'expected-missing-Play-native-adapter-rejected'),
        ('reload',('--reload',),'partial-native-ordinary-Play-input-reload'),
        ('missing-reload',('--reload','--negative-skip-reload'),'expected-missing-explicit-input-reload-rejected')]:
        add('play-input-'+name,'tools/sdk/check_native_input_play.py',status,'E/native-input-play',args,resource='desktop-unity',timeout=1500)
    native_args = ('--worker','{worker}','--author','{input-author}')
    native_deps = ('worker','input-author')
    for negative in (False, True):
        test('native_input_host','expected-missing-native-adapter-rejected' if negative else 'partial-native-preview-Host-author',
             'native-input-host',native_args+(('--negative-no-adapter',) if negative else ()),
             deps=native_deps,id='preview-host-'+str(negative),resource='desktop',required={'host_exited':True})
    desktop = native_args+('--desktop','--background-injection')
    def native(name,args=(),status='partial-native-desktop-Host-author',required=None,timeout=240):
        test('native_input_host',status,'native-desktop-input-host',desktop+tuple(args),deps=native_deps,
             id='desktop-'+name,resource='desktop',timeout=timeout,
             required=required if required is not None else {'host_exited':True,'result.author_durable_revision':3,'result.stop_exit':True})
    native('base')
    native('no-adapter',('--negative-no-adapter',),'expected-missing-native-adapter-rejected',{'host_exited':True,'result.author_input_absent':True})
    native('cleanup-negative',('--negative-cleanup-fault',),'expected-native-cleanup-fault-rejected',{'host_exited':True,'result.cleanup_fault_detected':True})
    for mode in ('move','wheel'):
        native('pending-'+mode,('--pending-interruption',mode))
    for mode in ('control','move','wheel'):
        native('inflight-'+mode,('--inflight-reply',mode))
        if mode!='control':
            native('inflight-negative-'+mode,('--inflight-reply',mode,'--negative-inflight-omit-revoke'),
                   'expected-omitted-inflight-revoke-rejected',{'host_exited':True,'result.omitted_inflight_revoke_detected':True})
    for mode in ('control','revoke'):
        native('queued-'+mode,('--queued-notification',mode))
    native('queued-negative',('--queued-notification','revoke','--negative-queued-ignore-token'),
           'expected-ignored-queued-token-rejected',{'host_exited':True,'result.ignored_queued_token_detected':True})
    native('manual-recovery',('--recover-helper',))
    native('manual-active-recovery',('--recover-helper','--recover-helper-active'))
    auto=('--automatic-recovery',)
    native('automatic',auto)
    for mode in ('mouse','key','cleanup','timeout-active','timeout-pending','heartbeat-exit'):
        native('automatic-'+mode,auto+('--automatic-boundary',mode))
    for mode in ('ready','mouse','close','connecting'):
        native('window-'+mode,auto+('--window-destruction',mode))
    native('expired-activation',auto+('--expired-activation',))
    native('expiry-after-validation',auto+('--expired-activation','--expiry-after-validation'))
    native('heartbeat-expiry',auto+('--heartbeat-expiry',))
    native('heartbeat-lease-expiry',auto+('--heartbeat-expiry','--heartbeat-lease-expiry'))
    native('heartbeat-stale-negative',auto+('--heartbeat-expiry','--negative-heartbeat-stale'),
           'expected-stale-heartbeat-rejected',{'host_exited':True,'result.stale_heartbeat_detected':True})
    for mode in ('focus','publish'):
        native('evidence-'+mode,auto+('--native-evidence-expiry',mode))
    for mode in ('continuous-motion','continuous-unmapped'):
        native(mode,auto+('--'+mode,))
    native('idle-1800',auto+('--soak-seconds','1800'),required={'host_exited':True,'result.author_durable_revision':3,'result.stop_exit':True,'result.soak.seconds':1800},timeout=1920)
    for name,args,status in [('injector',('--injector-self-test',),'partial-owned-injector-self-test'),
                              ('trace',('--trace-self-test',),'partial-acquisition-trace-self-test'),
                              ('trace-negative',('--trace-self-test','--negative-trace-sleep'),'expected-sleeping-pump-rejected')]:
        native(name,args,status,{'source_unchanged':True})
    for mode in ('base','unmapped','pid-publication','creation','creation-negative'):
        add('owned-timeout-'+mode,'Supervisor/Tests/run_owned_input_timeout.py',
            'expected-nonatomic-job-rejected-and-cleaned' if mode=='creation-negative' else 'partial-actual-client-kill-release-positive-and-negative',
            'E/owned-input-timeout',('{desktop-base}',)+(() if mode=='base' else ('--'+mode,)),('desktop-base',),
            resource='desktop',sources_required=False,required={'without_outer_cleanup_held':True})
    continuity_args=('--worker','{worker}','--author','{continuity-author}')
    add('continuity-preflight','Supervisor/Tests/check_input_continuity_inputs.py','passed','E/input-continuity-inputs',
        continuity_args,('worker','continuity-author'),timeout=600,result_key='cases',
        result_cases=('mixed--compile-only','mixed--trace-self-test','mixed--injector-self-test','mixed--diagnostic-worker',
                      'actual-copies-positive','exporter-source','compiler-source','author-build-source','project-builder-source',
                      'missing-author-dependency','removed-author-dependency','worker-build-source','missing-worker-dependency',
                      'removed-worker-dependency','metadata-file','canonical-build','author-status','worker-status','diagnostic-worker'))
    continuity_args+=('--desktop','--background-injection','--interaction-seconds')
    for name,seconds,extra,status in [
        ('short','10',(),'partial-native-desktop-Host-author'),
        ('drop-release','10',('--interaction-drop-release',),'expected-omitted-continuity-edge-rejected')]:
        required={'host_exited':True,'interaction_seconds':10,'interaction_scope':'development-short-not-formal',
                  'interaction_drop_release':name=='drop-release'}
        if name=='short':required.update({'result.stop_exit':True,'interaction_proof.requested_seconds':10,
                                         'interaction_proof.mode':'development-short-only','interaction_proof.owed':0})
        else:required['result.omitted_continuity_edge_detected']=True
        test('native_input_host',status,'native-desktop-input-host',continuity_args+(seconds,)+extra,
             deps=('worker','continuity-author','continuity-preflight'),id='continuity-'+name,
             required=required,role='continuity' if name=='short' else '',resource='desktop',timeout=240)
    for key,bit in zip(CONTINUITY_KEYS,(1,4,8,16,32,64,2,128)):
        add('continuity-kill-'+key.lower(),'Supervisor/Tests/run_owned_input_timeout.py',
            'partial-actual-client-kill-release-positive-and-negative','E/owned-input-timeout',
            ('{continuity-short}','--key',key),('continuity-short',),resource='desktop',sources_required=False,
            required={'key_name':key,'ledger_bit':bit,'held_before_kill':True,'ledger_after_kill':bit,
                      'without_outer_cleanup_held':True,'native_released':True,'ledger_after_cleanup':0,
                      'client_kernel_exit_observed':True,'outer_job_assigned':True,'outer_job_active_zero':True,'cleanup_errors':[]})
    for negative in (False,True):
        test('native_input_host','expected-omitted-continuity-edge-rejected' if negative else 'partial-native-desktop-Host-author',
             'native-desktop-input-host',continuity_args+('10','--interaction-mixed-motion')+(('--interaction-drop-release',) if negative else ()),
             deps=('worker','continuity-author','continuity-preflight'),id='continuity-mixed'+('-drop-release' if negative else ''),
             resource='desktop',timeout=240,
             required={'host_exited':True,'interaction_seconds':10,'interaction_mixed_motion':True,'interaction_drop_release':negative,
                       **({'result.omitted_continuity_edge_detected':True} if negative else {'result.stop_exit':True,'interaction_proof.mixed_motion':True,'interaction_proof.owed':0})})
    test('native_input_host','partial-native-desktop-Host-author','native-desktop-input-host',continuity_args+('1800',),
         deps=INTERACTION_REQUIRED[:-1],id='continuity-1800',resource='desktop',timeout=1980,role='continuity',
         required={'host_exited':True,'result.stop_exit':True,'interaction_seconds':1800,'interaction_drop_release':False,
                   'interaction_scope':'formal-1800s','interaction_proof.requested_seconds':1800,
                   'interaction_proof.mode':'formal-1800s','interaction_proof.dropped_edges':0,'interaction_proof.owed':0})
    for mode in ('base','negative-cached-snapshot','kill-owner'):
        test('thread_sampling','partial-owned-thread-sampling-control',args=() if mode=='base' else ('--'+mode,),id='thread-sampling-'+mode)
    for name,status in [('audio_features','partial-coarse-features-only'),('audio_sampler','partial-production-native-audio-sampler-only'),
                         ('audio_broker','partial-managed-native-audio-broker-only'),('audio_lifecycle','partial-real-audio-owner-lifecycle'),
                         ('audio_tree','partial-production-audio-tree'),('audio_stability','partial-controlled-author-replay')]:
        required = ({key:True for key in ('lateChildIncluded','childSurvivedParentAtRevoke','childrenExited',
                                         'descendantsExited','readerStopped','sourceStable')} if name == 'audio_tree'
                    else {'sourceStable':True} if name == 'audio_lifecycle' else {})
        test(name,status,sources_required=name in ('audio_tree','audio_stability','audio_lifecycle'),
             role='audio-observation' if name == 'audio_lifecycle' else '',required=required)
    for name in ('audio_timing','audio_broker_faults'):
        test(name,'',sources_required=False,required={'exitCode':0})
    from audio_race_evidence import NEGATIVES, status as audio_race_status
    for negative in (None,*NEGATIVES):
        test('audio_races',audio_race_status(negative),
             id='audio-races'+('-negative-'+negative if negative else ''),
             args=('--negative',negative) if negative else (),role='audio-races',timeout=150)
    test('audio_stability','failed',args=('--negative-no-latch',),id='audio-stability-negative',nonzero=True,
         required={'negativeNoLatch':True,'cases':14},nonzero_fields=('exitCode',),contains={'stderr':'stability-latch-band-'})
    audio_args=('--worker','{worker}','--author','{audio-author}','--require-stable-zero')
    for name,extra in [('direct',()),('tree',('--tree-source',)),('pause',('--pause-sampler',))]:
        test('audio_host','partial-configured-audio-Host',args=audio_args+extra,deps=('worker','audio-author'),id='audio-host-'+name,
             role='audio-observation',required={'childrenExited':True,'samplerExited':True,'cleanupErrors':[]})
    test('audio_host','not-complete',args=audio_args+('--pause-sampler','--negative-no-pause'),deps=('worker','audio-author'),
         id='audio-host-pause-negative',nonzero=True,negative='sampler-watchdog-kernel-exit-missing',
         required={'childrenExited':True,'samplerExited':True,'cleanupErrors':[],'samplerObservation.pauseInjected':False,'samplerObservation.exitedBeforeFallback':False})
    test('audio_host','not-complete',args=('--worker','{worker}','--author','{audio-negative-author}','--require-stable-zero','--negative-stability-verdict'),
         deps=('worker','audio-negative-author'),id='audio-host-verdict-negative',nonzero=True,
         required={'childrenExited':True,'samplerExited':True,'cleanupErrors':[]},
         log_contains={'client.log':'author-stable-zero-proof-rejected'})
    for name,status in [('failure','partial-audio-Play-Broker-crash-only'),('reload','partial-ordinary-Play-audio-stop-reload-restart')]:
        clean={'childrenExited':True}
        clean.update({key:True for key in (('brokerExited','samplerExited') if name=='failure' else ('oldBrokerExited','oldSamplerExited','newBrokerExited','newSamplerExited'))})
        add('audio-play-'+name,'tools/sdk/check_audio_play_'+name+'.py',status,'E/audio-play-'+name,
            resource='unity',timeout=1500,required=clean)
    add('audio-play-failure-negative','tools/sdk/check_audio_play_failure.py','not-complete','E/audio-play-failure',
        ('--negative-keep-broker',),resource='unity',timeout=1500,nonzero=True,negative='Broker crash audio clear deadline',
        required={'clearDeadlineExceeded':True,'sourceAliveAtDeadline':True,'brokerAliveAtDeadline':True,'samplerAliveAtDeadline':True,'childrenExited':True,'brokerExited':True,'samplerExited':True})
    add('audio-play-reload-negative','tools/sdk/check_audio_play_reload.py','not-complete','E/audio-play-reload',
        ('--negative-skip-reload',),resource='unity',timeout=1500,nonzero=True,negative='Play reload marker deadline: reloaded',
        required={'markerDeadline':'reloaded','childrenExited':True,'oldBrokerExited':True,'oldSamplerExited':True})
    test('llm','partial-LLM-real-HTTP-core-only')
    test('llm_host','partial-configured-LLM-Host',args=('--worker','{worker}','--author','{llm-author}'),deps=('worker','llm-author'))
    add('d-regression','tools/sdk/verify_d.py','passed-stage-D-resource-supervision-only','D/acceptance',resource='unity',timeout=14400)
    # Pure shared plan avoids registry -> runner -> registry recursion.
    import json
    from pathlib import Path
    from input_cycle_plan import schedule
    policy=json.loads(Path(__file__).with_name('release-policy.json').read_text())
    cycle_plan=schedule(policy);by_id={row.id:row for row in rows}
    cycle_timeout=sum(by_id[step['case']].timeout for step in cycle_plan)+by_id['worker'].timeout+by_id['input-author'].timeout+300
    add('input-cycles','tools/sdk/input_cycles.py','passed-input-cycle-gate-only','E/input-cycles',
        role='input-cycles',resource='desktop-unity',timeout=cycle_timeout,
        required={'source_unchanged':True,'final_evidence_unchanged':True,'configuration_identity_unchanged':True})
    return rows
