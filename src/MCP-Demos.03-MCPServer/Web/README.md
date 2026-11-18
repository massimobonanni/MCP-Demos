# Customize the Stage Director dashboard

The `welcome`, `focus`, `energy`, and `celebration` scenes share one HTML page. When the scene changes, [stage.js](./stage.js) reads the current state from `/api/stage` and updates the page. There is no separate HTML file for each scene.

## What to edit

| What you want to change | Where to edit it |
|---|---|
| Shared markup, such as the header, stage layout, or decorative elements | [index.html](./index.html) |
| Fonts, spacing, lighting, or animations | [styles.css](./styles.css). The stage's `data-effect` and `data-intensity` attributes let you style different effects and intensity levels. |
| An individual scene's title, subtitle, colors, or effect | [StageSceneCatalog.cs](../Domain/StageSceneCatalog.cs). The title and subtitle in the HTML are placeholders that the dashboard replaces with live text. |

When rearranging the markup, keep the IDs used by the script: `stage`, `title`, `subtitle`, `scene`, `connection`, `connection-detail`, `stale`, and `confetti`. Keep the accessibility attributes on the live text and status regions, and put new styles and scripts in the existing CSS and JavaScript files rather than inline.

## Example: Add a badge to the focus scene

1. In [index.html](./index.html), add this element inside `<div class="screen">`, after the subtitle:

   ```html
   <p id="focus-badge" hidden>Technical deep dive</p>
   ```

2. In [stage.js](./stage.js), add this line inside `render(snapshot)`, after the subtitle update. It shows the badge in `focus` and hides it again when you switch scenes:

   ```javascript
   document.getElementById('focus-badge').hidden = snapshot.scene !== 'focus';
   ```

## Preview your changes

The web assets are embedded in `StageDirector.dll`, so saving an HTML or CSS file and refreshing the browser is not enough.

1. Stop **stage-director** in VS Code if it is running.
2. From the repository root, rebuild with the .NET 10 SDK:

   ```powershell
   dotnet build .\src\MCP-Demos.03-MCPServer\StageDirector.csproj -c Release
   ```

3. [Start **stage-director** again](../README.md#usage) and refresh <http://127.0.0.1:5187/> (or use your configured port). Switch to `focus` and then another scene to check that the badge appears and disappears.
