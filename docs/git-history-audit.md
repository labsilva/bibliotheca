# Git history audit before publication

This is an investigation record, not confirmation that the history is safe to
publish. Issue #75 tracks the cleanup. Do not publish the repository until the
full history audit and any required remediation are complete.

## Findings

On 2026-11-22, inspection of locally available branches and tags found historic
SQLite files at `src/LABSoft.Bibliotheca.Backend/wwwroot/data/Bibliotheca.db`
and `src/Bibliotheca.Backend/wwwroot/data/Bibliotheca.db`, together with WAL
and SHM files at the older path. They are absent from the current tracked tree
but remain in historical Git objects. Both identified database snapshots were
opened locally, including the available WAL and SHM files; their user and
user-book tables had no rows. One inspected snapshot contained 27 catalogue
books and cover images. No catalogue content or credential was printed.

The available historical appsettings JSON files were checked for nonempty
values under common secret-related key names; this limited check found none.
A further local signature scan checked 1,653 historical text blobs for PEM
private-key headers, common GitHub token formats and AWS access-key formats;
it found no matches. It skipped 83 binary blobs and two blobs over 2 MB.
Neither check detects all possible secrets, including custom formats, binary
files, commit messages, remote-only refs, GitHub caches, or forks. Perform an
independent full-history secret scan before concluding the audit. Rotate any
actual exposed secrets, including values no longer present in the current tree.

## Cleanup plan

1. Inventory all local and remote branches, tags, pull-request references,
   forks, and archives. Secure a private backup and record owners of existing
   clones. Assess the historical database and other flagged objects without
   disclosing their content in logs or public issues.
2. In a **fresh isolated clone**, use a maintained Git history filtering tool to
   remove the historical database, WAL and SHM paths and any other confirmed
   sensitive objects from every relevant branch and tag. Do not merely delete
   the files in a new commit. Keep the original backup private.
3. Before replacing remote references, compare commit, branch and tag mappings;
   verify builds and tests on the rewritten tree; scan all rewritten objects;
   confirm no excluded object remains reachable. Review the exact refs to be
   updated and agree a coordination window with clone owners.
4. History rewriting and force-updating published references are destructive:
   obtain explicit approval for the concrete rewrite and force-push. Afterwards
   coordinate fresh clones or carefully reset existing clones, prevent old refs
   from being pushed back, and request GitHub support for any cached or
   inaccessible references that cannot be removed by a normal push.
5. Rotate compromised credentials and verify that backups, forks, caches and
   other copies are handled separately. History rewriting cannot erase data
   already copied by others. Re-run the scan and confirm `.gitignore` and
   `.dockerignore` cover local data and keys.
