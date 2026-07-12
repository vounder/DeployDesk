## Summary

Describe the user-visible or technical outcome and why it is needed.

## Verification

- [ ] `dotnet build DeployDesk.sln --configuration Release`
- [ ] WPF smoke test
- [ ] Security-validation smoke test when parsing, paths, processes, URLs, or trust are affected
- [ ] English and German UI checked when user-facing text changes
- [ ] `git diff --check`

## Safety and compatibility

- [ ] No credentials, private infrastructure details, generated artifacts, or personal paths are included
- [ ] Existing user changes and local state compatibility are preserved
- [ ] Schema, runtime validation, examples, and documentation agree
- [ ] No real deployment or server mutation was performed without explicit authorization
