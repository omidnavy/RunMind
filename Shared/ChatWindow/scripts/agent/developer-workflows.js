    'use strict';

    const DEVELOPER_WORKFLOW_KEY_PREFIX='runmind.developer-workflows.v1.';
    const BUILTIN_DEVELOPER_WORKFLOWS=[
      {id:'understand-project',name:'Understand this project',description:'Map architecture, entry points, dependencies, and important execution paths.',prompt:'Analyze this project before proposing changes. Use the project index if available; otherwise build it. Inspect manifests and the most relevant source files. Explain the architecture, entry points, dependency boundaries, important data/control flows, tests, and risks. Cite exact relative file paths and symbols. Do not modify files.',builtIn:true},
      {id:'implement-feature',name:'Implement a feature',description:'Plan, implement, validate, and review a multi-file change.',prompt:'Implement the requested feature in this project. First inspect the project index, architecture, relevant files, conventions, and tests. Create a concise plan. Make the smallest coherent set of changes, preserve existing behavior, and request approval for every write through the existing file tools. Run project_validate for bounded static manifest checks and test-file discovery. Do not claim builds, lint, or tests ran: RunMind has no general command-execution bridge. Review the complete diff and report changed files, validation performed, and remaining risks.',builtIn:true},
      {id:'debug-issue',name:'Investigate a bug',description:'Trace a reported failure to its likely root cause and propose a verified fix.',prompt:'Investigate the reported bug. Search the project index for relevant symbols and call sites, trace the actual execution path, inspect tests and error handling, and identify a root cause supported by source evidence. If a fix is requested, plan it, make the smallest approved change, and review the diff. Do not claim runtime reproduction unless actually performed.',builtIn:true},
      {id:'review-changes',name:'Review repository changes',description:'Review working-tree changes for correctness, regressions, and security issues.',prompt:'Perform a code review of the current repository changes. Read Git status and diff first, then inspect surrounding implementation and tests. Prioritize concrete correctness, security, compatibility, and regression findings. Report findings by severity with file and line references. Do not modify files or stage/commit anything.',builtIn:true},
      {id:'prepare-release',name:'Prepare a release',description:'Audit version metadata, changelog, tests, packaging, and release readiness.',prompt:'Prepare a release-readiness assessment. Inspect project metadata, version references, changelog, roadmap, build/package scripts, tests, and Git status. Run project_validate for static manifest checks and test-file discovery; do not claim build/test commands were executed. Identify inconsistencies and missing release checks. Do not change version numbers, create tags, commit, or publish unless I explicitly ask. Provide a concise release checklist with evidence.',builtIn:true}
    ];
    function developerWorkflowKey(){return `${DEVELOPER_WORKFLOW_KEY_PREFIX}${workspaceContext.id}`;}
    async function loadDeveloperWorkflows(){
      const saved=await workspaceDbAction('readonly','get',undefined,developerWorkflowKey());
      const custom=Array.isArray(saved)?saved.filter(item=>item&&typeof item.id==='string'&&typeof item.name==='string'&&typeof item.prompt==='string'&&!item.builtIn).slice(0,40):[];
      return [...BUILTIN_DEVELOPER_WORKFLOWS,...custom];
    }
    async function saveDeveloperWorkflow({id=null,name,prompt,description=''}){
      if(!agentDirectoryHandle) throw new Error('Select a project workspace before saving a project workflow.');
      const cleanName=String(name||'').trim().slice(0,80),cleanPrompt=String(prompt||'').trim().slice(0,12000);
      if(!cleanName||!cleanPrompt) throw new Error('A workflow name and instruction are required.');
      const rows=(await loadDeveloperWorkflows()).filter(item=>!item.builtIn);
      const workflowId=id&&/^[a-z0-9-]{1,80}$/.test(id)?id:`custom-${crypto.randomUUID()}`;
      const item={id:workflowId,name:cleanName,description:String(description||'').trim().slice(0,240),prompt:cleanPrompt,builtIn:false,updatedAt:new Date().toISOString()};
      const next=[...rows.filter(row=>row.id!==workflowId),item].slice(-40);
      await workspaceDbAction('readwrite','put',next,developerWorkflowKey());
      return item;
    }
    async function deleteDeveloperWorkflow(id){
      const rows=(await loadDeveloperWorkflows()).filter(item=>!item.builtIn&&item.id!==id);
      await workspaceDbAction('readwrite','put',rows,developerWorkflowKey());
      return {deleted:id,remaining:rows.length};
    }
    async function startDeveloperWorkflow(id,context=''){
      const workflow=(await loadDeveloperWorkflows()).find(item=>item.id===id);
      if(!workflow) throw new Error(`Developer workflow '${id}' was not found.`);
      return {workflow:{id:workflow.id,name:workflow.name,description:workflow.description},instructions:workflow.prompt,userContext:String(context||'').slice(0,8000),workspace:agentDirectoryHandle?.name||workspaceContext.folderName||null,execution:'Uses the current RunMind Agent runtime and its existing approval, cancellation, task-history, Skills, workspace, and Git capabilities. No separate workflow executor is created.'};
    }
