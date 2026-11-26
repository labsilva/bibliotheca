---
name: bibliotheca-engineer
description: Turn a working prototype or pilot into a maintainable solution that fits the repository architecture. Use to refactor, harden, or prepare an experimental implementation for production and future extension.
---

# Engineer a prototype into a maintained feature

Read [AGENTS.md](../../../AGENTS.md), the [agent guide](../../../docs/agent-guide.md), and any available [prototype handoff](../bibliotheca-prototype/SKILL.md#handoff). Inspect the working tree, the normal entry points, the prototype evidence, and the existing architecture. When there is no handoff, reconstruct the behavior and its known gaps from the code and tests.

1. Identify the supported behavior, compatibility constraints, and failure modes. Run the smallest relevant baseline and add characterization tests where a refactor could change unprotected behavior. Confirm the scope of intentional changes from the user's request.
2. Find the existing boundaries for domain decisions, I/O, configuration, lifecycle, and persistence. Preserve the contracts callers rely on and reuse existing abstractions. Introduce a new extension point when it isolates a demonstrated source of change. Define a concrete extension scenario that will show whether the design works.
3. Refactor in increments that can be verified independently. Put validation where trusted code receives data, keep credentials in the host, and give failures actionable diagnostics. Apply deadline, cleanup, concurrency, and consistency rules where the operation requires them. Retain compatibility with existing configuration and data, or implement the migration and rollback that the requested change needs.
4. Verify the result through the normal application path. Exercise the success and relevant failure cases, including fallback, retries, or cancellation when applicable. Run the repository checks for the affected layers. Keep fixture evidence, live evidence, and target-platform evidence distinct.
5. Report what is ready, what changed at the architectural boundaries, and what remains unverified. Link the relevant checks and state any intentional compatibility or rollout decisions. Follow [bibliotheca-deliver](../bibliotheca-deliver/SKILL.md) for an authorized GitHub delivery.

For Bibliotheca's extractors, separate package discovery and registration from script execution and outbound I/O. Keep ISBN and author-evidence checks in trusted code. An extension test can install a fixture provider for an existing capability and resolve it through the usual service without editing `Program.cs`. A compatibility check can exercise an existing manifest and the script replacement and restart workflow.
