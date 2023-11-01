# SwimWhitelistPlugin
## Features
* Reach out to an API to check if the user has certain Discord roles
* Can reserve certain amount of general slots
* Can reserve certain amount of slots per car
## Configuration
Enable the plugin in `extra_cfg.yml`
```yaml
EnablePlugins:
- SwimWhitelistPlugin
```
Example configuration (add to bottom of `extra_cfg.yml`)  
**All patterns are regular expressions. If you don't know what regular expressions are I'd highly recommend reading about them first.**
```yaml
---
---
!SwimWhitelistConfiguration
EndpointUrl: http://localhost:8000/checksteamid
ReservedSlots: 2
ReservedSlotsRoles:
  - 735193787999059969
ReservedCars:
  - { Model: ks_mazda_rx7_spirit_r, Amount: 1, Roles: [1111111111] }
```