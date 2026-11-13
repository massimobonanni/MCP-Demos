# DEMO 02 - Chat console using an LLM and MCP tools

## Description

This demo is a C# console application (.NET 10) that provides a chat between the user and an LLM hosted on Microsoft Foundry. It uses the Microsoft Learn MCP server (https://learn.microsoft.com/en-us/training/support/mcp) as a tool.

## Features

The demo implements the following workflow:

1. On startup, create a client to connect to the model in Microsoft Foundry and configure the Microsoft Learn MCP server tools using an MCP client.
2. In a loop, prompt the user for input and send it to the model, preserving the conversation history between turns.
3. End the chat when the user enters `/quit`.

For educational purposes, the console should display the full interaction flow: user messages, model responses, MCP tool calls, and the corresponding tool results.

## Requirements

- A .NET 10 console project named "MCP-Demos.02-MCPChat"
- The project is located in the `src` folder
- The project contains a `readme.md` file describing the demo, its structure, the class diagram, and how to run it.
- The console application connects to Microsoft Foundry using an endpoint and an API key, both stored in the project's .NET user secrets.
- Use the `Microsoft.Extensions.AI` libraries for inference with the LLM.
- The application provisions a resource group, a Microsoft Foundry resource, and a project within Microsoft Foundry, then deploys the `gpt-6-luna` model using the model's default deployment settings supported in the selected region.
- Use the standard AZD environment variables `AZURE_ENV_NAME`, `AZURE_SUBSCRIPTION_ID`, and `AZURE_LOCATION` for the environment name, subscription, and deployment location. Include the environment name in the resource group name.
- After provisioning, AZD must configure the project's .NET user secrets with the Microsoft Foundry model endpoint and API key so the application can connect when run locally.
- Define a system message to guide the model's interactions.