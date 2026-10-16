# The Ripple Effect engine, for RippleFx.Engine

The native engine the [`RippleFx.Engine`](https://www.nuget.org/packages/RippleFx.Engine)
bindings call. Reference it beside them, and **exactly one** of the two:

```sh
dotnet add package RippleFx.Engine
dotnet add package RippleFx.Engine.Native            # with Python
# or
dotnet add package RippleFx.Engine.Native.NoPython   # without: smaller, and plays every
                                                     # campaign exported with "Needs no Python"
```

It carries the engine for Windows, macOS and Linux on x64 and arm64, Android
on arm64 and x86_64, and iOS on device and simulator (statically linked).
Each version pins one signed engine release; `MAJOR.MINOR` is the engine
contract it binds.

The engine is under the Ripple Effect Runtime Licence, which lets you
redistribute it unmodified in function as part of your own software, free
of charge: see `LICENSE-ENGINE.txt`, and reproduce `ENGINE-NOTICE.txt`
wherever your product lists its third-party software.

Source, documentation and issues:
https://github.com/ConnectingTheDots-DCWK/ripple_effect_csharp_integration
