# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`
- **Read an issue**: `gh issue view <number> --comments`, also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply/remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close an issue**: `gh issue close <number> --comment "..."`

Infer the repository from `git remote -v`; `gh` does this automatically inside the clone.

## Pull requests as a triage surface

**PRs as a request surface: no.**

Set this to `yes` if the repository later treats external pull requests as feature requests.

## When a skill says “publish to the issue tracker”

Create a GitHub issue.

## When a skill says “fetch the relevant ticket”

Run `gh issue view <number> --comments`.

## Wayfinding operations

The map is a single issue, with child issues used as tickets.

- **Map**: An issue labelled `wayfinder:map`, holding Notes, Decisions-so-far, and Fog.
- **Child ticket**: A GitHub sub-issue linked to the map. If sub-issues are unavailable, add it to a task list in the map and begin the child with `Part of #<map>`. Use a `wayfinder:<type>` label: `research`, `prototype`, `grilling`, or `task`.
- **Blocking**: Prefer GitHub’s native issue dependencies. If unavailable, begin the child with `Blocked by: #<n>, #<n>`.
- **Frontier query**: Select the first open, unassigned child without an open blocker, in map order.
- **Claim**: `gh issue edit <n> --add-assignee @me`
- **Resolve**: Comment with the answer, close the child, and append a context pointer to the map’s Decisions-so-far.
