# Xdeck wordmark font

`xdeck-brand-bold.woff2` is the unmodified Latin subset of Inter Bold (weight 700), obtained from
Google Fonts via <https://fonts.googleapis.com/css2?family=Inter:wght@700&display=swap>.
Inter is copyright The Inter Project Authors and licensed under the SIL Open Font License 1.1.
The complete license is included in `OFL.txt`; upstream: <https://github.com/rsms/inter>.

The desktop panel and phone PWA bundle the same font bytes and alias the face as `Xdeck Brand` in
CSS. This gives the wordmark a consistent weight and appearance on Windows, Android and iOS without
requiring a font download from a third-party service at runtime. Other interface text keeps the
platform's normal UI font.
