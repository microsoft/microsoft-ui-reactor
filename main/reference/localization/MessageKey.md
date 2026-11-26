---
title: "MessageKey"
description: "Identifies a localizable message by namespace and key. Namespace corresponds to the .resw file name (e.g., \"Common\" for Common.resw). Key is the data name within the .resw file."
---

```csharp
public readonly record struct MessageKey(string Namespace, string Key)
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](/microsoft-ui-reactor/main/guides/localization)

## Constructors [#constructors]

### `MessageKey` [#messagekey_1]

Identifies a localizable message by namespace and key. Namespace corresponds to the .resw file name (e.g., "Common" for Common.resw). Key is the data name within the .resw file.

```csharp
public MessageKey(string Namespace, string Key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Namespace` | `string` |  |
| `Key` | `string` |  |

## Properties [#properties]

### `Key` [#key]

```csharp
public string Key  // get, init
```

### `Namespace` [#namespace]

```csharp
public string Namespace  // get, init
```
