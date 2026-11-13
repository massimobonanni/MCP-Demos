# DEMO 01 - Creating an MCP Client for Microsoft Learn

## Description

This demo shows how to create an MCP client (using the MCP SDK for C#) for the MCP server provided by Microsoft Learn.
An overview of the MCP server is available at https://learn.microsoft.com/en-us/training/support/mcp.

## Features

The demo performs the following steps:

1) Creates an MCP client for the MCP server provided by Microsoft Learn
2) On startup, calls the MCP protocol's `tools/list` function and displays the available tools
3) Enters a loop that prompts the user for a search topic
4) Exits the loop when the user enters `/quit`
5) If the user enters a topic at the prompt, calls the `microsoft_docs_search` tool with the user's input (the `query` argument)
6) Displays the search results (the `title` and `contentUrl` fields)

## Requirements

- A .NET 10 console project named "MCP-Demos.01-MCPClient"
- The project is located in the `src` folder
- The project contains a `readme.md` file describing the demo, its structure, the class diagram, and how to run it.