# Security

Do not file public issues containing controller credentials, network captures with sensitive data, or exploitable deployment details. Use GitHub private vulnerability reporting once enabled by the repository owner.

UR TCP interfaces and the HTTP Robot API are not authenticated/encrypted by this library. Isolate robot networks, restrict listener access with firewall rules, and never expose these ports directly to the Internet. This library is not part of a safety-rated stop system.

Treat control scripts as trusted executable input. Script includes must remain within the top-level script directory; linked paths are rejected. Filesystem containment does not defend against another process concurrently replacing trusted files. Keep templates in a directory writable only by trusted operators.