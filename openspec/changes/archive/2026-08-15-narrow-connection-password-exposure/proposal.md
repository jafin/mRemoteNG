## Why

The upstream audit ranks this HIGH and uses it to conclude CVE-2023-30367 is not fixed. **Its
evidence does not apply to our fork**, and the residual it would have found instead is real but
small. Recording both is the point of this proposal: the finding will be raised again by the next
scan, and the answer should be written down once.

What the audit reports, against upstream's model:

```csharp
public virtual string Password
{
    get => GetPropertyValue("Password", _password);
    set => SetField(ref _password, value, "Password");     // plain string field
}
```

What our fork has, at `AbstractConnectionRecord.cs:370`:

```csharp
get => GetPropertyValue(nameof(Password), _password?.ConvertToUnsecureString() ?? string.Empty);
set => SetSecureStringField(ref _password, value, nameof(Password));
```

`_password`, `_rdGatewayPassword` and `_vncProxyPassword` are `SecureString`, zeroed and replaced on
assignment (`AbstractConnectionRecord.cs:1575-1586`). Decryption is deferred per field rather than
run eagerly over the whole config (`XmlConnectionsDeserializer.cs:323`). The audit's
`RDGatewayPassword:601` and `VNCProxyPassword:1073` citations are against line numbers that hold
different code here.

The genuine residual is at the boundaries, and it is narrower than a HIGH:

- The property type is still `string`, because the property grid binds to it. Every read materialises
  an immutable copy that cannot be zeroed and lives until collection.
- `RdpProtocol` takes one such copy into a local at line 978 and does not use it until line 1151,
  through the external-credential-provider branches in between. The window is long for no reason.
- The RDP COM surface takes a `string` (`AdvancedSettings2.ClearTextPassword`). That conversion
  cannot be removed, only made brief.

## What Changes

- A `SecureString` accessor for each secret, so callers that can consume one — SSH.NET, SFTP, file
  transfer — stop round-tripping through `string`. The `string` property stays for the property grid.
- `RdpProtocol` reads the password at the point it assigns it rather than 170 lines earlier.
- No change to the storage, which is already correct, and no attempt to remove the COM conversion,
  which cannot be.

## Capabilities

Adds `connection-record-secrets`. States what the connection record guarantees about secrets in
memory — which is currently true but undocumented, and therefore one refactor away from being
quietly given up.

## Impact

`mRemoteNG/Connection/AbstractConnectionRecord.cs`,
`mRemoteNG/Connection/Protocol/RDP/RdpProtocol.cs`, and the SSH-backed callers that would adopt the
new accessor.

**This is the lowest-value item of the six and should be scheduled last.** It shortens an exposure
window; `harden-connection-file-kdf`, `encrypt-sql-backend-with-aead` and
`replace-default-connection-file-key` are the difference between encrypted and encrypted in name
only. An attacker who can dump another process's memory has already won by most measures, which is
why `SecureString` is documented by Microsoft as not a security boundary.

The risk is disproportionate to the benefit if taken carelessly: `RdpProtocol`'s password handling
runs through external credential providers, Remote Credential Guard, restricted admin and gateway
paths, and breaking authentication there is far more damaging than the exposure being closed. Any
change to that method needs manual verification against a real RDP host, not just a green suite.
