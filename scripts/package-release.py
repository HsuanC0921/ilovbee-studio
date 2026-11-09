"""Package the clean, committed release only after build.ps1 -ReleaseChecks passes."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import tomllib
import zipfile


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default=str(root / 'target/packages'))
    args = parser.parse_args()
    out, stage = pathlib.Path(args.output).resolve(), root / 'dist'
    assert not out.is_relative_to(stage), 'Archive output must be outside dist.'
    assert not subprocess.check_output(['git', 'status', '--porcelain'], cwd=root), 'Commit release changes before packaging.'
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    version = tomllib.loads((root / 'Cargo.toml').read_text(encoding='utf-8-sig'))['package']['version']
    reports = {name: json.loads((stage / 'validation' / (name + '.json')).read_text(encoding='utf8'))
               for name in ['security-stress', 'ui', 'dependencies']}
    assert all(value['status'] == 'pass' for value in reports.values()), 'Release gate failed.'
    binary = stage / 'iLovbeeStudio.exe'
    assert reports['security-stress']['version'] == version
    assert hashlib.sha256(binary.read_bytes()).hexdigest() == reports['security-stress']['binary_sha256'], 'Staged binary differs from tested binary.'
    assert (stage / 'iLovbeeUI.exe').is_file()
    # Notices cover reachable Windows MSVC/GNU runtime and build dependencies.
    packages = {}
    for path in sorted((root / 'target/release-checks').glob('*-x86_64-pc-windows-*.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        nodes = {node['id']: node for node in data['resolve']['nodes']}
        pending, reached = [data['resolve']['root']], set()
        while pending:
            key = pending.pop()
            if key in reached:
                continue
            reached.add(key)
            pending.extend(dep['pkg'] for dep in nodes[key]['deps'])
        packages.update({(p['name'], p['version']): p for p in data['packages'] if p['id'] in reached and p['source']})
    assert packages, 'Missing locked dependency metadata.'
    notices = ['Locked Windows Rust dependencies, including build tools.',
               'WebView2Loader.dll: webview2-com-sys; WebView2 Runtime is installed separately.', '']
    for key, package in sorted(packages.items()):
        notices += ['=' * 72, ' '.join(key), 'License: ' + str(package['license']),
                    'Repository: ' + str(package['repository']), '']
        for file in sorted(pathlib.Path(package['manifest_path']).parent.iterdir()):
            if file.is_file() and file.name.lower().startswith(('license', 'licence', 'notice', 'copying', 'copyright')):
                notices += [file.name, file.read_text(encoding='utf8', errors='replace'), '']
    (stage / 'THIRD-PARTY-NOTICES.txt').write_text('\n'.join(notices), encoding='utf8')
    (stage / '開始使用.txt').write_text('解壓縮後執行 iLovbeeStudio.exe。\n介面左下可選跟隨 Windows、淺色或深色。\n先建立備份，再修改真實鍵盤。\n離線示範不存取真實鍵盤或音訊。\n功能與測試範圍見 README.zh-TW.md、docs/csharp-parity.md 與 docs/release-validation.md。\n', encoding='utf-8-sig')
    (stage / '離線示範.cmd').write_bytes(b'@echo off\r\nstart "" "%~dp0iLovbeeStudio.exe" --demo\r\n')
    manifest = {'version': version, 'commit': commit,
                'files': {name: hashlib.sha256((stage / name).read_bytes()).hexdigest()
                          for name in ['iLovbeeStudio.exe', 'iLovbeeUI.exe']}}
    (stage / 'VERSION.json').write_text(json.dumps(manifest, indent=2), encoding='utf8')
    out.mkdir(parents=True, exist_ok=True)
    portable = out / f'iLovbeeStudio-{version}-rust-windows-x64.zip'
    source = out / f'ilovbee-studio-{version}-rust-source.zip'
    with zipfile.ZipFile(portable, 'w', zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(stage.rglob('*')):
            if file.is_file():
                archive.write(file, pathlib.Path('iLovbeeStudio-' + version) / file.relative_to(stage))
    files = subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).decode().split('\0')
    with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(files):
            if name:
                archive.write(root / name, pathlib.Path('ilovbee-studio') / name)
        archive.write(stage / 'THIRD-PARTY-NOTICES.txt', 'ilovbee-studio/THIRD-PARTY-NOTICES.txt')
    digests = []
    for file in [portable, source]:
        with zipfile.ZipFile(file) as archive:
            assert archive.testzip() is None
        digests.append(hashlib.sha256(file.read_bytes()).hexdigest() + '  ' + file.name)
        print(file.name, file.stat().st_size)
    (out / 'SHA256SUMS.txt').write_text('\n'.join(digests) + '\n', encoding='ascii')
    print('PASS: tested binary hash, clean source commit, release reports, ZIP integrity')


if __name__ == '__main__':
    main()
