---
name: bibliotheca-create-extractor
description: Create, adapt, or validate a Bibliotheca extractor package for an approved source. Use for custom JavaScript extractors that can be installed or updated without recompiling the backend.
---

# Create a Bibliotheca extractor

Read [the extractor guide](../../../docs/javascript-extractors.md) and inspect
the installed package nearest to the requested capability. Identify the
fields the application needs and the pages or connector records that prove them.

1. Create a directory named after the provider with a version 1 `manifest.json` and `extract.js`. Declare the required capabilities and exact HTTPS origins. Keep activation and origin approval in deployment configuration. Use an existing backend connector when a source needs authenticated API access.
2. Start from a known edition or provider identifier. Confirm the ISBN before returning book metadata or a cover. For an author profile, verify the named credit on a known edition with `host.authorEvidence` before following the profile link. Let the backend record the observed source.
3. Validate with fixtures that cover a confirmed match and a wrong edition, ambiguous author, or blocked destination where relevant. Run `extractors check` with the deployment approvals, then verify the result through the usual application interface and identify the provider that actually supplied it.
4. Document the deployment settings and the update and rollback procedure. Record fixture and live results separately, including any provider access or credential blocker.

Keep scripts and fixtures free of deployment credentials and private captures.
