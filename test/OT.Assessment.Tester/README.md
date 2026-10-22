# Assessment tester

Set `Api:EndpointUri` in `appsettings.json` to the full URI of the endpoint that
accepts casino wagers in your implementation. No route is assumed or appended.
The value must be an absolute HTTP or HTTPS URI without a fragment; query
parameters are supported.

Run the tester with `dotnet run` from this directory after starting your API.
The tester sends 7,000 JSON POST requests at a rate of 500 requests every two
seconds, without a warm-up. HTTP 2xx responses count as successful; other HTTP
responses and transport errors count as failures.
