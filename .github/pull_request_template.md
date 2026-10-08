## Summary

Describe what changes and why.

## Validation

- [ ] Regression tests pass with warnings treated as errors.
- [ ] Repository/self-contained policy checks pass.
- [ ] New/changed downloads have an explicit integrity check.
- [ ] Archive extraction remains traversal-safe and bounded.
- [ ] User-visible UI changes support English and French.
- [ ] No duplicate install/download control was added for a centrally managed component.
- [ ] Dependency lock/provenance was updated for new or changed third-party source.
- [ ] License/notice obligations were reviewed.
- [ ] Large binaries/models are distributed through Releases rather than Git history.
- [ ] No token, credential, private SDK, machine-specific absolute path, or secret was committed.
- [ ] Documentation/manifests were updated where behavior or distribution changed.

## Runtime / network impact

List any new runtime network host, process execution, elevated action, model/runtime download, or local-machine mutation. Write `None` if there is no change.

## License impact

List new third-party code/models/assets and their licenses. Write `None` if there is no change.
