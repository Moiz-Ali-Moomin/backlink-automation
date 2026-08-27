# Template Pools and Variation

Template pools are typed as `WordPressComment`, `OwnedProperty`, or `GenericOwnedNetwork` and choose either website-field or comment-body placement. Pools support round-robin, deterministic random, and weighted deterministic random selection.

Templates support these placeholders: `{{target_url}}`, `{{target_domain}}`, `{{brand}}`, `{{display_name}}`, `{{source_domain}}`, and `{{source_title}}`. Each template may contain up to 100 controlled prefix, suffix, anchor, and target-URL variants. Variant choice uses the same campaign/source/attempt seed as identity choice, so preview and execution agree and retries remain auditable. Templates are never mutated nondeterministically and the engine does not generate an unbounded unique message for every source.

`submission_preview` (REST `/api/v1/submissions/preview`, CLI `submission preview`) resolves the adapter, ownership decision, source compatibility, identity, template, variants, rendered comment, placement method, and expected strategy without making a network request.
