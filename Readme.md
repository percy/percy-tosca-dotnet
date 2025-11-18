# PercyTosca

PercyTosca is a .NET project that produces a distributable assembly (`PercyTosca<Dotnet_Version>.dll`) which can be used in Tosca for calling PercySnapshot.

## Development

Install/update `@percy/cli` dev dependency (requires Node 14+):

```sh-session
$ npm install --save-dev @percy/cli
```

Set PERCY_TOKEN and start Percy CLI

```sh-session
$ set PERCY_TOKEN=<TOKEN>
$ percy exec:start
```

## Tosca

- Copy the DLL file downloaded from releases in `C:\Program Files (x86)\TRICENTIS\Tosca Testsuite\Percy`
- Add this path inside: Tosca Commander -> Project settings -> TBox -> Extension loading -> Extensions
- Create a module in Tosca Commander and define following configuration options:
    - Engine -> Percy
    - SpecialExecutionTask -> PercySnapshot
- Define parameters using the parameters below and define following configuration options:
    - Parameter -> True
- Install the tosca browser extension if not already done.
- Restart Tosca Commander and run the tests.

## Parameters

The snapshot method arguments:

- `SnapshotName` (**required**) - The snapshot name; must be unique to each snapshot
- Additional snapshot options (overrides any project options):
  - `Caption` - Title of your webpage (Default: "*") [string]
  - `Widths` - Comma seperated integer values to take screenshots at
  - `MinHeight` - The minimum viewport height to take screenshots at [integer]
  - `EnableJavaScript` - Enable JavaScript in Percy's rendering environment [boolean]
  - `PercyCSS` - Percy specific CSS only applied in Percy's rendering [boolean]
    environment
  - `Scope` - A CSS selector to scope the screenshot to [string]
