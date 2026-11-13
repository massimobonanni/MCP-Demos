# Plan: Demo 01 — MCP Client for Microsoft Learn

## Problem and approach

Implement the demo described in `docs/descriptions/demo01.md`: a .NET 10 console application named `MCP-Demos.01-MCPClient`, located under `src`, that connects to the official Microsoft Learn MCP server using its supported transport. On startup, the client lists the available tools, then accepts search topics, invokes `microsoft_docs_search` with the `query` parameter, and displays the `title` and `contentUrl` fields from the results.

The approved scope includes the console project and its `readme.md`; it excludes automated tests and implementation of an MCP server. The loop exits on `/quit`, ignores empty input, and reports errors without prematurely ending the session. Before choosing the endpoint and transport, verify the official Microsoft Learn documentation and use the C# Model Context Protocol SDK client APIs.

## Task progress

| Task number | Title | Completion status |
|---|---|---|
| 1 | Creating the demo project | to do |
| 2 | Implementing the MCP client | to do |
| 3 | Documenting the demo | to do |
| 4 | Validating the demo | to do |

## Tasks

1. Create the .NET 10 console project under `src` with the required name and add the necessary MCP client package references.
2. Implement the connection and interactive loop: list the available tools, handle `/quit` and empty input, call `microsoft_docs_search` with `query`, display `title` and `contentUrl`, and report errors explicitly.
3. Write `readme.md` with the demo overview, project structure, class diagram, and run instructions.
4. Verify package restore, build, and console startup; verify the remote connection and search only if the environment allows access to the service.

## Notes

- The repository does not yet contain projects under `src`; this demo will be the first application project.
- Follow the repository's C# and MCP instructions and keep logging separate from user-facing output.
- Do not embed credentials; choose the endpoint and any configuration according to the service's official guidance.
