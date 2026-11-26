---
name: create-skill
description: Design a reusable RunMind Agent Skill for a recurring workflow or specialized task.
---
# Create a Skill

1. Identify the recurring task, intended users, and the RunMind capabilities the skill may rely on. Ask one focused question only if the purpose or scope is unclear.
2. Check existing skills first; extend or improve an existing skill when that avoids duplicate guidance.
3. Use a concise kebab-case name matching the skill folder. Write a short description that explains when to use it without assuming hidden project context.
4. Structure `SKILL.md` as actionable workflow guidance: when to use, prerequisites/capability checks, steps, verification, safety boundaries, and limitations.
5. Reference supporting files by safe relative paths. Keep the skill self-contained and avoid unnecessary dependencies, scripts, external downloads, or duplicated documentation.
6. Never claim a tool or permission exists unless it appears in RunMind's current capability inventory. Skills guide behavior; native tools enforce authorization.
7. Validate frontmatter, folder/name agreement, referenced paths, and size limits. Review the complete skill for unsafe instructions before offering installation.
8. Ask before installing or replacing a skill. Preserve an existing skill unless the user explicitly approves replacement.
