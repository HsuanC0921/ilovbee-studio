use ilovbee_studio::{
    actions,
    api::{App, Session},
    lighting::{self, Palette, Visualizer, BANDS},
    live::{self, Config},
    model::*,
    protocol::*,
    service::{DemoTransport, Service},
    store::{BackupSettings, Store},
};
use serde_json::json;
use std::sync::{Arc, Mutex};

fn store() -> (tempfile::TempDir, Store) {
    let dir = tempfile::tempdir().unwrap();
    let s = Store::new(dir.path().to_path_buf()).unwrap();
    (dir, s)
}
fn mac(name: &str, events: &[(u8, bool, i32)]) -> Macro {
    Macro {
        name: name.into(),
        events: events
            .iter()
            .map(|&(code, up, delay)| MacroEvent { code, up, delay })
            .collect(),
    }
}
fn t1() -> Macro {
    mac(
        "T1",
        &[(4, false, 10), (4, true, 100), (5, false, 10), (5, true, 0)],
    )
}

#[test]
fn captured_packet_headers_and_command_allowlist() {
    assert_eq!(
        hex::encode_upper(&read_request(0x83, 0, 416).unwrap()[..8]),
        "068300000100A001"
    );
    assert_eq!(
        hex::encode_upper(&write_request(10, 0, &vec![0; 420]).unwrap()[..8]),
        "060A00000100A401"
    );
    for (command, layer, count) in [(0x83, 4, 416), (0x84, 1, 128), (0x87, 0, 128), (0x85, 0, 1)] {
        assert!(read_request(command, layer, count).is_err());
    }
    for (command, layer, n) in [
        (0, 0, 416),
        (3, 4, 416),
        (4, 1, 128),
        (5, 0, 513),
        (8, 0, 377),
        (10, 0, 419),
        (0xff, 0, 1),
    ] {
        assert!(write_request(command, layer, &vec![0; n]).is_err());
    }
    assert!(packet(0x83, 0, 0).is_err());
    assert!(packet(0x83, 0, 513).is_err());
}
#[test]
fn readback_requires_exact_header_and_report_size() {
    let req = read_request(0x83, 3, 416).unwrap();
    let mut res = req;
    res[8] = 42;
    assert_eq!(reply(&req, &res).unwrap()[0], 42);
    res[2] = 0;
    assert!(reply(&req, &res).is_err());
    assert!(reply(&req, &req[..519]).is_err());
}
#[test]
fn vendor_single_macro_fixture() {
    assert_eq!(
        hex::encode_upper(encode_macros(&[t1()]).unwrap()),
        "04001500045400310000000A048000640400000A0580000005"
    );
}
#[test]
fn vendor_two_macros_with_modifiers_fixture() {
    let t2 = mac(
        "T2",
        &[
            (0xe1, false, 10),
            (6, false, 10),
            (6, true, 30),
            (0xe1, true, 0),
        ],
    );
    assert_eq!(hex::encode_upper(encode_macros(&[t2,t1()]).unwrap()),"080015001D001500045400320010000AE100000A0680001E06900000E1045400310000000A048000640400000A0580000005");
}
#[test]
fn macro_validation_capacity_names_and_delays() {
    for m in [
        mac("", &[(4, false, 0)]),
        mac("x", &[]),
        mac("x", &[(0, false, 0)]),
        mac("x", &[(4, false, -1)]),
        mac("x", &[(4, false, 0x100000)]),
    ] {
        assert!(encode_macros(&[m]).is_err());
    }
    assert!(encode_macros(&vec![t1(); 9]).is_err());
    assert!(encode_macros(&vec![mac("x", &vec![(4, false, 0); 60]); 8]).is_err());
    assert!(encode_macros(&[mac("😀😀😀😀😀😀😀😀😀😀", &[(4, false, 0xfffff)])]).is_ok());
    assert!(encode_macros(&[mac("😀😀😀😀😀😀😀😀😀😀😀", &[(4, false, 0)])]).is_err());
}
#[test]
fn decode_chords_media_and_macro_references() {
    assert_eq!(actions::decode(&actions::code(4, 5), &[]), "Ctrl＋Alt＋A");
    assert_eq!(actions::decode(&[2, 0, 1, 0x92], &[]), "小算盤");
    assert_eq!(actions::decode(&[3, 1, 1, 0], &[t1()]), "巨集：T1");
    assert_eq!(actions::decode(&[3, 1, 1, 7], &[]), "巨集 #8");
    assert!(actions::all(&[])
        .iter()
        .any(|(n, b)| n == "音量提高" && b == &[2, 0, 0, 0xe9]));
}
#[test]
fn captured_fn_functions_can_be_assigned_without_changing_other_keys() {
    // Physical Fn1-layer captures, 2026-11-09. Values are device actions,
    // not USB commands; this tests assignment/readback, not physical actuation.
    let captures = [
        ("側燈切色（鍵盤內建）", "08000001"),
        ("機內背光錄製／儲存（鍵盤內建）", "08070000"),
        ("機內背光 1（可錄製）", "08092000"),
        ("機內背光 2（固定）", "08092100"),
        ("機內背光 3（固定）", "08092200"),
        ("Win Lock 切換（鍵盤內建）", "07000001"),
        ("切換 Windows 模式（鍵盤內建）", "0700000E"),
        ("切換 Mac 模式（鍵盤內建）", "0700000F"),
    ];
    let (_dir, s) = store();
    let mut service = Service::new(s, true);
    for (name, captured) in captures {
        let code = bytes(captured, 4).unwrap();
        assert_eq!(actions::decode(&code, &[]), name);
        assert!(actions::all(&[])
            .iter()
            .any(|(n, c)| n == name && c == code.as_slice()));
        let before = service.capture().unwrap();
        let mut desired = before.keymaps.clone();
        let mut map = bytes(&desired[&0], MAP_BYTES).unwrap();
        map[78 * 4..79 * 4].copy_from_slice(&code);
        desired.insert(0, hex::encode_upper(map));
        service.apply_maps(&before.keymaps, &desired, &[]).unwrap();
        let after = service.capture().unwrap();
        assert_eq!(after.keymaps, desired);
        assert_eq!(after.lighting, before.lighting);
    }
}
#[test]
fn keyboard_settings_preserve_unrelated_bytes_and_reject_stale_snapshots() {
    let (_dir, s) = store();
    let mut app = App::new(s, true).unwrap();
    let before = app.service.capture().unwrap();
    // Unknown/reserved fields and the selected onboard pattern must survive.
    let mut initial = bytes(&before.lighting, LIGHT_BYTES).unwrap();
    initial[3] = 3;
    initial[9] = 1;
    initial[11] = 0x20;
    initial[25] = 0x91;
    app.service
        .set_lights(&bytes(&before.lighting, LIGHT_BYTES).unwrap(), &initial)
        .unwrap();
    let expected = hex::encode_upper(&initial);
    let request =
        json!({"expected":expected,"settings":{"systemMode":"mac","winLock":true,"sideColor":10}});
    app.route("POST", "/api/keyboard", "", request.to_string().as_bytes())
        .unwrap();
    let changed = app.service.capture().unwrap();
    let mut desired = initial.clone();
    desired[0] = 1;
    desired[15] = 1;
    desired[18] = 10;
    assert_eq!(changed.lighting, hex::encode_upper(&desired));
    assert_eq!(changed.keymaps, before.keymaps);
    assert!(app
        .route("POST", "/api/keyboard", "", request.to_string().as_bytes())
        .unwrap_err()
        .to_string()
        .contains("已變更"));
    assert_eq!(app.service.capture().unwrap().lighting, changed.lighting);
    let back = json!({"expected":changed.lighting,"settings":{"systemMode":"windows","winLock":false,"sideColor":0}});
    app.route("POST", "/api/keyboard", "", back.to_string().as_bytes())
        .unwrap();
    assert_eq!(app.service.capture().unwrap().lighting, expected);
}
#[test]
fn invalid_keyboard_settings_do_not_write_or_create_backups() {
    let (_dir, s) = store();
    let mut app = App::new(s, true).unwrap();
    let before = app.service.capture().unwrap();
    for settings in [
        json!({"sideColor":11}),
        json!({"sideColor":-1}),
        json!({"systemMode":"linux"}),
        json!({"winLock":1}),
        json!({"recording":true}),
    ] {
        let request = json!({"expected":before.lighting,"settings":settings});
        assert!(app
            .route("POST", "/api/keyboard", "", request.to_string().as_bytes())
            .is_err());
    }
    assert_eq!(app.service.capture().unwrap().lighting, before.lighting);
    assert!(app.service.store.backups().unwrap().is_empty());
}
#[test]
fn legacy_profile_and_four_layers_round_trip() {
    let (_dir, s) = store();
    let mut service = Service::new(s, true);
    let mut p = service.capture().unwrap();
    p.validate().unwrap();
    let text = serde_json::to_string(&p).unwrap();
    assert!(text.contains("\"Keymaps\""));
    let restored: Profile = serde_json::from_str(&text).unwrap();
    restored.validate().unwrap();
    p.keymaps.remove(&3);
    p.validate().unwrap();
    p.device = "342D:E469".into();
    assert!(p.validate().is_err());
}
#[test]
fn malformed_profiles_are_rejected() {
    let (_dir, s) = store();
    let mut service = Service::new(s, true);
    let p = service.capture().unwrap();
    let mut bad = p.clone();
    bad.keymaps.remove(&1);
    assert!(bad.validate().is_err());
    let mut bad = p.clone();
    bad.keymaps.insert(0, "FF".into());
    assert!(bad.validate().is_err());
    let mut bad = p.clone();
    bad.lighting = "GG".repeat(128);
    assert!(bad.validate().is_err());
    let mut bad = p;
    bad.schema = 2;
    assert!(bad.validate().is_err());
}
#[test]
fn applying_maps_preserves_other_layers_and_fn() {
    let (_dir, s) = store();
    let mut service = Service::new(s, true);
    let before = service.capture().unwrap();
    let mut desired = before.keymaps.clone();
    let mut m = bytes(&desired[&1], 416).unwrap();
    m[78 * 4..78 * 4 + 4].copy_from_slice(&actions::code(7, 8));
    desired.insert(1, hex::encode_upper(m));
    service.apply_maps(&before.keymaps, &desired, &[]).unwrap();
    let after = service.capture().unwrap();
    assert_eq!(after.keymaps, desired);
    assert_eq!(after.lighting, before.lighting);
    assert_eq!(service.store.backups().unwrap().len(), 1);
}

#[derive(Default)]
struct FaultState {
    writes: Vec<(u8, u8)>,
    fail_at: Option<usize>,
    fail_rollback: bool,
    corrupt_read: bool,
}
struct FaultTransport {
    inner: DemoTransport,
    state: Arc<Mutex<FaultState>>,
}
impl Transport for FaultTransport {
    fn read(&mut self, c: u8, l: u8, n: usize) -> anyhow::Result<Vec<u8>> {
        let mut data = self.inner.read(c, l, n)?;
        let state = self.state.lock().unwrap();
        if state.corrupt_read && !state.writes.is_empty() && state.writes.len() == 1 {
            data[0] ^= 1;
        }
        Ok(data)
    }
    fn write(&mut self, c: u8, l: u8, d: &[u8]) -> anyhow::Result<()> {
        self.inner.write(c, l, d)?;
        let mut s = self.state.lock().unwrap();
        s.writes.push((c, l));
        if s.fail_at == Some(s.writes.len()) || (s.fail_rollback && s.writes.len() > 1) {
            anyhow::bail!("injected write failure");
        }
        Ok(())
    }
}
fn faulty(store: Store, state: Arc<Mutex<FaultState>>) -> Service {
    Service::with_transport(
        store,
        Box::new(FaultTransport {
            inner: DemoTransport::default(),
            state,
        }),
    )
}
fn changed(p: &Profile) -> Maps {
    let mut d = p.keymaps.clone();
    for i in 0..2 {
        let mut b = bytes(&d[&i], 416).unwrap();
        b[0] = 1;
        d.insert(i, hex::encode_upper(b));
    }
    d
}
#[test]
fn write_failure_rolls_back_attempted_layers_in_reverse_order() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState {
        fail_at: Some(2),
        ..Default::default()
    }));
    let mut service = faulty(s, state.clone());
    let before = service.capture().unwrap();
    let err = service
        .apply_maps(&before.keymaps, &changed(&before), &[])
        .unwrap_err();
    assert!(err.to_string().contains("已回復"));
    assert_eq!(service.capture().unwrap().keymaps, before.keymaps);
    assert_eq!(
        state.lock().unwrap().writes,
        vec![(3, 0), (3, 1), (3, 1), (3, 0)]
    );
}
#[test]
fn rollback_failure_is_observable() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState {
        fail_at: Some(1),
        fail_rollback: true,
        ..Default::default()
    }));
    let mut service = faulty(s, state);
    let before = service.capture().unwrap();
    let err = service
        .apply_maps(&before.keymaps, &changed(&before), &[])
        .unwrap_err();
    assert!(err.to_string().contains("回復未能確認"));
}
#[test]
fn mismatched_readback_rolls_back_without_disk_backups() {
    let (_dir, s) = store();
    s.save_backup_settings(&BackupSettings {
        auto: false,
        ..Default::default()
    })
    .unwrap();
    let state = Arc::new(Mutex::new(FaultState {
        corrupt_read: true,
        ..Default::default()
    }));
    let mut service = faulty(s, state);
    let before = service.capture().unwrap();
    assert!(service
        .apply_maps(&before.keymaps, &changed(&before), &[])
        .is_err());
    assert_eq!(service.capture().unwrap().keymaps, before.keymaps);
    assert!(service.store.backups().unwrap().is_empty());
}
#[test]
fn fn_protection_in_apply_and_restore_precedes_writes() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState::default()));
    let mut service = faulty(s, state.clone());
    let before = service.capture().unwrap();
    let mut p = before.clone();
    let mut m = bytes(&p.keymaps[&0], 416).unwrap();
    m[59 * 4] = 0;
    p.keymaps.insert(0, hex::encode_upper(m));
    assert!(service
        .apply_maps(&before.keymaps, &p.keymaps, &[])
        .is_err());
    assert!(service.restore(&p).is_err());
    assert!(state.lock().unwrap().writes.is_empty());
}
#[test]
fn stale_snapshot_rejects_macro_upload_before_any_write() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState::default()));
    let mut service = faulty(s, state.clone());
    let mut p = service.capture().unwrap();
    let mut old = bytes(&p.keymaps[&0], 416).unwrap();
    old[0] = 99;
    p.keymaps.insert(0, hex::encode_upper(&old));
    let mut desired = p.keymaps.clone();
    old[4..8].copy_from_slice(&[3, 1, 1, 0]);
    desired.insert(0, hex::encode_upper(old));
    assert!(service.apply_maps(&p.keymaps, &desired, &[t1()]).is_err());
    assert!(state.lock().unwrap().writes.is_empty());
}
#[test]
fn invalid_macro_reference_never_writes() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState::default()));
    let mut service = faulty(s, state.clone());
    let p = service.capture().unwrap();
    let mut desired = p.keymaps.clone();
    let mut m = bytes(&desired[&0], 416).unwrap();
    m[..4].copy_from_slice(&[3, 1, 1, 7]);
    desired.insert(0, hex::encode_upper(m));
    assert!(service.apply_maps(&p.keymaps, &desired, &[t1()]).is_err());
    assert!(state.lock().unwrap().writes.is_empty());
}
#[test]
fn valid_macro_is_uploaded_before_mapping() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState::default()));
    let mut service = faulty(s, state.clone());
    let p = service.capture().unwrap();
    let mut desired = p.keymaps.clone();
    let mut m = bytes(&desired[&0], 416).unwrap();
    m[..4].copy_from_slice(&[3, 1, 1, 0]);
    desired.insert(0, hex::encode_upper(m));
    service.apply_maps(&p.keymaps, &desired, &[t1()]).unwrap();
    assert_eq!(state.lock().unwrap().writes, vec![(5, 0), (3, 0)]);
}
#[test]
fn lighting_transaction_rolls_back_on_failed_readback() {
    let (_dir, s) = store();
    let state = Arc::new(Mutex::new(FaultState {
        corrupt_read: true,
        ..Default::default()
    }));
    let mut service = faulty(s, state);
    let before = service.read_lights().unwrap();
    let mut after = before.clone();
    after[10] = 2;
    assert!(service.set_lights(&before, &after).is_err());
    assert_eq!(service.read_lights().unwrap(), before);
}
#[test]
fn old_three_layer_restore_preserves_tap_layer() {
    let (_dir, s) = store();
    let mut service = Service::new(s, true);
    let before = service.capture().unwrap();
    let mut p = before.clone();
    p.keymaps.remove(&3);
    let mut m = bytes(&p.keymaps[&0], 416).unwrap();
    m[4] = 1;
    p.keymaps.insert(0, hex::encode_upper(m));
    service.restore(&p).unwrap();
    assert_eq!(service.capture().unwrap().keymaps[&3], before.keymaps[&3]);
}
#[test]
fn backup_retention_keeps_manual_and_automatic_separate() {
    let (_dir, s) = store();
    let mut service = Service::new(s.clone(), true);
    let p = service.capture().unwrap();
    s.save_backup_settings(&BackupSettings {
        auto: true,
        keep_auto: 2,
        keep_manual: 1,
    })
    .unwrap();
    for i in 0..4 {
        s.save(&format!("backups/000{i}-before-keymaps.json"), &p)
            .unwrap();
        s.save(&format!("backups/000{i}-manual.json"), &p).unwrap();
    }
    s.prune(&s.backup_settings().unwrap()).unwrap();
    assert_eq!(
        s.backups().unwrap(),
        vec![
            "0003-manual.json",
            "0003-before-keymaps.json",
            "0002-before-keymaps.json"
        ]
    );
}
#[test]
fn backup_path_traversal_is_rejected() {
    let (_dir, s) = store();
    for name in ["../x.json", "..\\x.json", "C:x.json", "x.json/..", "x.txt"] {
        assert!(s.load_backup(name).is_err());
    }
}
#[test]
fn legacy_bom_config_and_library_are_read_without_data_loss() {
    let (_dir, s) = store();
    s.write_text(
        "macros.json",
        &format!("\u{feff}{}", serde_json::to_string(&vec![t1()]).unwrap()),
    )
    .unwrap();
    let app = App::new(s.clone(), true).unwrap();
    assert_eq!(app.macros[0].name, "T1");
    s.write_text("schemes.json", "{ invalid").unwrap();
    assert!(App::new(s.clone(), true).is_err());
    assert_eq!(
        s.read_text("schemes.json", 100).unwrap().unwrap(),
        "{ invalid"
    );
}
#[test]
fn atomic_save_replaces_existing_json() {
    let (_dir, s) = store();
    s.save("macros.json", &vec![t1()]).unwrap();
    s.save("macros.json", &Vec::<Macro>::new()).unwrap();
    assert!(s.load::<Vec<Macro>>("macros.json").unwrap().is_empty());
}
#[test]
fn custom_color_preserves_other_slots_and_saves_previous_table() {
    let (_dir, s) = store();
    let mut service = Service::new(s.clone(), true);
    let before = service.color_table().unwrap();
    assert_eq!(&before[..3], &[0, 0, 0]);
    assert_eq!(&before[42..45], &[255, 0, 0]);
    service.set_color(2, &[0, 0, 204]).unwrap();
    let after = service.color_table().unwrap();
    assert_eq!(&after[42..45], &[0, 0, 204]);
    assert_eq!(&after[45..], &before[45..]);
    assert_eq!(
        hex::decode(s.read_text("colortable-before.hex", 1024).unwrap().unwrap()).unwrap(),
        before
    );
}
#[test]
fn hsv_and_animated_key_effects_preserve_source() {
    assert_eq!(lighting::hsv(0., 1., 1.), [255, 0, 0]);
    assert_eq!(lighting::hsv(120., 1., 1.), [0, 255, 0]);
    assert_eq!(lighting::hsv(240., 1., 1.), [0, 0, 255]);
    let mut frame = vec![0; 378];
    for s in 0..4 {
        frame[s * 3] = 200;
    }
    let mut fx = vec![0; 126];
    fx[..4].copy_from_slice(&[0, 1, 2, 3]);
    let a = lighting::animate(&frame, &fx, 0.4 / 0.7, 2);
    assert_eq!(a[0], 200);
    assert_eq!(a[3], 200);
    assert!((150..200).contains(&a[6]));
    assert_eq!(frame[6], 200);
    assert_eq!(lighting::animate(&frame, &fx, 0.6 / 0.7, 2)[3], 0);
    assert_eq!(*a[9..12].iter().max().unwrap(), 200);
}
#[test]
fn gradient_stops_and_white_peak_accents() {
    let p = Palette {
        mode: 1,
        stops: vec![[255, 0, 0], [0, 255, 0], [0, 0, 255]],
        direction: 0,
    };
    assert_eq!(p.pick(0., 1., 1., 0.), [255, 0, 0]);
    assert_eq!(p.pick(0., 1., 1., 0.5), [0, 255, 0]);
    assert_eq!(p.pick(0., 1., 1., 1.), [0, 0, 255]);
    assert_eq!(p.pick(0., 1., 1., 0.25), [128, 128, 0]);
    assert_eq!(p.pick(0., 0., 1., 0.), [255; 3]);
}
#[test]
fn twelve_visualizers_handle_real_keyboard_and_dynamic_spectra() {
    for style in 0..12 {
        let mut viz = Visualizer::new(keys());
        let mut lit = false;
        for step in 1..=120 {
            let bands = std::array::from_fn(|i| {
                if step % 25 < 5 {
                    0.
                } else {
                    ((i * 17 + step * 7) % 100) as f64 / 100.
                }
            });
            let frame = viz.render(style, &bands, step as f64 / 30., &Palette::default());
            assert_eq!(frame.len(), 378);
            lit |= frame.iter().any(|&b| b > 0);
        }
        assert!(lit, "style {style} never lit");
    }
    assert!(Visualizer::new(keys())
        .render(0, &[0.; BANDS], 0., &Palette::default())
        .iter()
        .all(|&b| b == 0));
}
#[test]
fn spectrum_detects_sine_near_expected_frequency() {
    let target = 8;
    let freq = 60. * 200_f64.powf((target as f64 + 0.5) / 18.);
    let samples: Vec<_> = (0..2048)
        .map(|i| (2. * std::f64::consts::PI * freq * i as f64 / 48000.).sin() * 0.01)
        .collect();
    let b = lighting::spectrum(&samples, 48000., 40.);
    let peak = b
        .iter()
        .enumerate()
        .max_by(|a, b| a.1.total_cmp(b.1))
        .unwrap()
        .0;
    assert_eq!(peak, target);
    assert_eq!(
        lighting::spectrum(&vec![0.; 2048], 48000., 40.),
        [0.; BANDS]
    );
}
#[test]
fn live_ini_roundtrip_and_failed_config_preserves_saved_state() {
    let (_dir, s) = store();
    let mut c = Config {
        mode: "custom".into(),
        ..Config::default()
    };
    c.frame[375] = 255;
    c.fx[125] = 3;
    c.save(&s).unwrap();
    let r = Config::load(&s).unwrap();
    assert_eq!(r.mode, "custom");
    assert_eq!(r.frame, c.frame);
    assert_eq!(r.fx, c.fx);
    c.colors = vec!["INVALID".into()];
    assert!(c.save(&s).is_err());
    assert_eq!(Config::load(&s).unwrap().colors, r.colors);
}
#[test]
fn live_mode_restores_original_and_retains_marker_on_failure() {
    let (_dir, s) = store();
    let mut service = Service::new(s.clone(), true);
    let before = service.read_lights().unwrap();
    live::enter(&mut service).unwrap();
    assert_eq!(service.read_lights().unwrap()[10], 18);
    assert!(s.root.join("live-restore.txt").exists());
    live::recover(&mut service).unwrap();
    assert_eq!(service.read_lights().unwrap(), before);
    assert!(!s.root.join("live-restore.txt").exists());
}
#[test]
fn session_blocks_wrong_host_origin_and_missing_cookie() {
    let s = Session {
        port: 1234,
        token: "ABC".into(),
    };
    let host = Some("127.0.0.1:1234");
    assert_eq!(
        s.authorize(host, None, None, "GET", "/", "t=ABC"),
        Some(true)
    );
    assert_eq!(
        s.authorize(
            host,
            None,
            Some("other=x; ilv=ABC"),
            "GET",
            "/api/state",
            ""
        ),
        Some(false)
    );
    assert_eq!(
        s.authorize(
            Some("evil.test:1234"),
            None,
            Some("ilv=ABC"),
            "POST",
            "/api/keymap",
            ""
        ),
        None
    );
    assert_eq!(
        s.authorize(
            host,
            Some("https://evil.test"),
            Some("ilv=ABC"),
            "POST",
            "/api/keymap",
            ""
        ),
        None
    );
    assert_eq!(
        s.authorize(host, None, None, "GET", "/api/state", "t=ABC"),
        None
    );
    assert_eq!(s.authorize(host, None, None, "POST", "/", "t=ABC"), None);
}
#[test]
fn scheme_and_layout_apis_validate_before_persistence() {
    let (_dir, s) = store();
    let mut app = App::new(s.clone(), true).unwrap();
    app.state().unwrap();
    let call = |app: &mut App, path: &str, v: serde_json::Value| {
        app.route("POST", path, "", &serde_json::to_vec(&v).unwrap())
    };
    call(&mut app, "/api/schemes/create", json!({"name":"工作"})).unwrap();
    assert_eq!(app.schemes.len(), 1);
    assert!(call(&mut app, "/api/schemes/create", json!({"name":"工作"})).is_err());
    call(
        &mut app,
        "/api/schemes/rename",
        json!({"index":0,"name":"遊戲"}),
    )
    .unwrap();
    assert_eq!(app.schemes[0].name, "遊戲");
    assert!(call(
        &mut app,
        "/api/layouts/put",
        json!({"name":"x","frame":"00"})
    )
    .is_err());
    assert!(app.layouts.is_empty());
    call(
        &mut app,
        "/api/layouts/put",
        json!({"name":"紅","frame":"FF0000".repeat(126),"fx":"01".repeat(126)}),
    )
    .unwrap();
    let reloaded = App::new(s, true).unwrap();
    assert_eq!(reloaded.layouts[0].fx.as_ref().unwrap(), &"01".repeat(126));
    assert_eq!(reloaded.schemes[0].name, "遊戲");
}
