# Review duplicate authors

Catalogue managers can open **Settings → Database → Duplicate authors** and start a scan. The status shows the number of authors checked and the number of new suggestions. The SQLite database stores the active scan, its last committed author and the suggestions. If the API restarts, the worker continues from that point. Authors added after the scan starts are included in the next scan.

The scanner compares names after removing whitespace and punctuation and applying Unicode canonical normalization and invariant uppercase. It preserves accents and name order. It may miss spelling variants, alternative names or reversed name order. A match can describe two different people, including homonyms. Compare the linked books, photos, biographies and any external author IDs before choosing an action. The scan never merges authors.

- **Merge authors** moves all book credits to the selected record and removes the other author. Books credited to both authors retain a single credit. Book records, shelves, ratings, reading status, favorites and comments are unaffected.
- **Defer** moves the pair to the deferred queue, where a manager can review it later.
- **Reject suggestion** suppresses that pair across scans. When a merge changes the canonical ID of a rejected author, the rejection follows the surviving ID. A newly created author record can still produce a new suggestion.

The selected record retains its catalogue ID, name and populated profile fields. When the external author IDs conflict, the selected profile is kept as a whole. Otherwise, missing profile fields can be filled from the removed profile. A copied biography keeps its original citation, and identical biographies can share a missing citation. An external author ID is copied only if the selected record did not already have profile content or an external author ID.

The scan and its review decisions are shared by catalogue managers. A merge is transactional and requires a fresh comparison. A merge that would overlap a duplicate scan or either catalogue refresh is blocked while that job runs. If another manager resolves or changes the comparison first, the API prompts you to reload it.

For incident review, the database stores the two original author IDs, the last review decision, reviewer and time, and the retained author ID for merges. The operational log records the suggestion ID, outcome and retained ID. Take a consistent SQLite backup before bulk review. An incorrect merge can be recovered by restoring that backup, which also restores the catalogue and reader data to the backup point. Use a separate restored copy for investigation if newer changes need to be preserved.
