# Core.Ioc

Lightweight IoC with stateful containers and editor tooling for service migration

> **Status:** WIP

Runtime instance registration is supported for cases where an object already exists and should be exposed through the container:

```csharp
var container = new ServiceContainerManager();

container.RegisterInstance<IHud>(hud);
container.RegisterInstance(hud);
```

---

# Part of the Core Project

This package is part of the **Core** project, which consists of multiple Unity packages.
See the full project here: [Core](https://github.com/Fur-Fighters-Frenzy/Core)

---
