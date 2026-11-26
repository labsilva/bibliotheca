---
name: bibliotheca-prototype
description: Build a small runnable prototype to test feasibility, compare approaches, or get fast feedback on an idea. Use for spikes, proofs of concept, pilots, and requests to prototype a feature or integration.
---

# Prototype an idea

Read [AGENTS.md](../../../AGENTS.md) and the [agent guide](../../../docs/agent-guide.md). Find the existing feature entry point and the nearest working example. Keep the experiment scoped to the uncertainty the user wants to resolve.

1. State the question and what observable result would answer it. Pick a representative input and, where it matters, one failure case. Use a reversible assumption for routine gaps and record it.
2. Build the smallest runnable slice that answers the question. Reuse the existing path when the question is about integration. Use a fixture or temporary harness when the question is narrower. Keep experimental work out of production defaults where practical. Preserve authentication, input validation, secret handling, and existing data safeguards.
3. Run the experiment and inspect the actual outcome. Use local fixtures for repeatability. Try a live integration when it materially affects the answer and the environment allows it. Record which provider or component produced the result when fallback is possible. Distinguish an implementation failure from an environment or upstream blocker.
4. Report whether the evidence supports proceeding, changing approach, or stopping. Include the smallest reproduction that would let another engineer check the conclusion. Use [bibliotheca-engineer](../bibliotheca-engineer/SKILL.md) when the user asks to mature the prototype.

## Handoff

Keep the handoff short and include it in the work summary. Save it separately only when the task calls for a durable record.

- **Question and outcome**. The hypothesis, result, and level of confidence.
- **Reproduction**. Changed entry point, commands, inputs, and results. Identify fixture, live, and target-platform checks as applicable.
- **Engineering work**. Temporary shortcuts, open decisions, known failure modes, and behavior worth preserving.

For an extractor spike, one ISBN with a confirmed match and one mismatched edition can establish that the script and host work together. A successful import after fallback only establishes the import result. Check the source of the metadata or cover before attributing success to the pilot.
