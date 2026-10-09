# AI Studio v4.5 — ComfyUI local endpoint preflight (experimental)

**Owner:** GPT C. **Status:** pure validation foundation only, not an installed or running AI backend.

`AiStudioComfyUiEndpointPolicy.TryValidate` validates a proposed API base URI **without making network requests or starting processes**. It accepts only the numeric loopback IPv4 address `http://127.0.0.1:8188/` or IPv6 `http://[::1]:8188/` (port may vary from 1024 to 65535). The endpoint must be HTTP with a root path, and have no username/password, query string, fragment or surrounding/control whitespace.

Rejected: `localhost` and arbitrary DNS names (no DNS resolution), LAN/Internet/wildcard hosts, non-loopback IPs, HTTPS or WebSocket schemes, privileged ports, extra paths and credential-bearing URLs. The caller must not reinterpret a rejection as permission to connect to a fallback remote host.

**Security boundary:** A valid URI is **not** proof that the listener is ComfyUI, that it is bound only to loopback, or that its subprocess and custom nodes are trusted. Before any actual HTTP invocation, a separately reviewed executor must enforce process ownership, local-listener binding, pinned runtime/hash+license gates, timeouts, bounded payloads, cancellation, workflow/node allowlists, no hidden downloads and safe job-state transitions. This class does none of those operations.

This increment does not alter v4.0, the Download Center, package distributors, model licenses or existing jobs. Its unit tests are entirely offline and do not require Python, ComfyUI, PyTorch, CUDA, RTX or any other third-party runtime.
