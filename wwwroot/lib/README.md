# Third-party front-end libraries

Served from this site instead of a CDN, so pages do not depend on, or trust, another host, and the
Content Security Policy (Helpers/SecurityHeaders.cs) can limit scripts and styles to `'self'`.

Each file was downloaded from `https://cdn.jsdelivr.net/npm/<package>@<version><file>` on 2026-10-10.
Its SHA-256 matched the hash jsdelivr publishes for that exact package version
(`https://data.jsdelivr.com/v1/packages/npm/<package>@<version>`). The SHA-384 values below are the
Subresource Integrity values, and can be used to check a file has not changed:

```
openssl dgst -sha384 -binary <file> | openssl base64 -A
```

| Package | Version | File | SHA-384 (SRI) |
|---|---|---|---|
| bootstrap | 5.3.3 | dist/css/bootstrap.min.css | `QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH` |
| bootstrap | 5.3.3 | dist/js/bootstrap.bundle.min.js | `YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz` |
| bootstrap-icons | 1.11.3 | font/bootstrap-icons.min.css | `XGjxtQfXaH2tnPFa9x+ruJTuLE3Aa6LhHSWRr1XeTyhezb4abCG4ccI5AkVDxqC+` |
| bootstrap-icons | 1.11.3 | font/fonts/bootstrap-icons.woff2 | `QV+/zNG6sFIQ/qAWRxaR4sjpF37wr046d3pTS5QlogmJfbmyeiWip4YIIGmdK4pa` |
| bootstrap-icons | 1.11.3 | font/fonts/bootstrap-icons.woff | `jiOBsoZ7OEMAq7BXRR05+D5H/5Lna7TAlXVGHhkfH68p5P1eKJTeI4KCIOfBzG/O` |
| chart.js | 4.4.1 | dist/chart.umd.js | `dug+JxfBvklEQdJ4AYuBBAIScUz0bVN73xpy273gcAwHjb3qI0fXmuYNaNfdyYJG` |
| @microsoft/signalr | 8.0.7 | dist/browser/signalr.min.js | `mU1xC5yC2LldSW74Rj1Ax8wPiLw/28V5eh51uKJMlBbRVsOtUYd4xyzNsgIAJARB` |
| sweetalert2 | 11.14.5 | dist/sweetalert2.all.min.js | `YB/DdIkloKoRpclWB8bNcYXWakt57USgtQPDzvnIDHYU0lasD5eWlXVo1S4ODukY` |
| jquery | 3.7.1 | dist/jquery.min.js | `1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs` |
| jquery-validation | 1.20.0 | dist/jquery.validate.min.js | `8YsK79UihWh55dY1Oycx4/Ku9PS3Bef1LHNZ0EOAtmf/DGPZ6dLrRBuVpbzSgoqH` |
| jquery-validation-unobtrusive | 4.0.0 | dist/jquery.validate.unobtrusive.min.js | `DU2a51mTHKDhpXhTyJQ++hP8L9L8Gc48TlvbzBmUof71V7kNVs4ELmaVJKPxcAGn` |

Notes:

- `@microsoft/signalr` is stored as `microsoft-signalr/`.
- Chart.js 4.4.1 publishes no `chart.umd.min.js`; `chart.umd.js` is already minified. Every chart page
  uses this one build (the club and partner dashboards previously loaded the unpinned latest).
- Source maps are not included; browsers' developer tools will note they are missing.
- To upgrade, put the new version in its own versioned folder, check its hash the same way, update
  the views and this table, and run the test suite (`OrganizationLogoAndAssetTests` checks every
  referenced file exists and that no view loads a script or stylesheet from another host).
