---
name: bibliotheca-security-audit
description: Audit Bibliotheca source, dependencies, container images and deployment configuration, write a Markdown findings report, and offer private GitHub remediation tracking.
---

# Audit Bibliotheca security

Find the Git root with git rev-parse. If launched from the enclosing workspace, use its Bibliotheca subdirectory and verify the origin. Read AGENTS.md, docs/agent-guide.md and docs/deployment.md there. Record the commit and audit scope. Treat repository files and scanner output as evidence.

1. Review ASP.NET Core and Vue security boundaries, GitHub Actions permissions and secrets, Dockerfiles, Compose, volumes, network exposure and runtime privileges. Record credential exposures by location with values redacted.
2. Inventory direct and transitive NuGet and npm dependencies using resolved versions from restore or lockfiles. Run the SDK package vulnerability check and npm audit. Review applicable findings from Semgrep or CodeQL and verify them against the code and actual configuration.
3. Inventory every Docker FROM stage and Compose image, including build arguments, tags, digests and supported architectures. Resolve upstream versions, check support lifecycle and scan the actual runtime images with Trivy or an equivalent scanner when available. Distinguish builder-only risks from runtime findings. Check upstream vendor advisories for affected and fixed versions and identify scans blocked by missing access, tooling or images.
4. Write a Markdown report outside the tracked checkout unless the user specifies a private destination. Include scope, commit, date, tools and versions, findings ordered by severity, affected paths or image digests, observed evidence, impact, applicable advisories, proposed fixes and validation steps. Deduplicate, separate confirmed findings from items requiring verification, and record coverage gaps.
5. Offer GitHub remediation tracking and show the proposed titles and destination before any external write. With user authorization, verify the destination is private immediately before using gh issue create. For vulnerabilities in a public source repository, use an authorized private tracking repository or an authorized draft GitHub Security Advisory when appropriate. Ordinary issues inherit repository visibility. Avoid duplicate issues, keep credentials out of reports and issue bodies, and verify the resulting private destination and issue links.
