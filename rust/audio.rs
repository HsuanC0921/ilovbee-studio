use anyhow::Result;
use std::collections::VecDeque;
use wasapi::{
    AudioCaptureClient, AudioClient, DeviceEnumerator, Direction, SampleType, StreamMode,
    WaveFormat,
};
pub struct Audio {
    capture: AudioCaptureClient,
    client: AudioClient,
    ring: VecDeque<f64>,
    _com: Com,
}
struct Com;
impl Drop for Com {
    fn drop(&mut self) {
        wasapi::deinitialize();
    }
}
impl Audio {
    pub fn new() -> Result<Self> {
        wasapi::initialize_mta().ok()?;
        let com = Com;
        let device = DeviceEnumerator::new()?.get_default_device(Direction::Render)?;
        let mut client = device.get_iaudioclient()?;
        let format = WaveFormat::new(32, 32, SampleType::Float, 48000, 2, None);
        client.initialize_client(
            &format,
            Direction::Capture,
            StreamMode::PollingShared {
                autoconvert: true,
                buffer_duration_hns: 1_000_000,
            },
            None,
        )?;
        let capture = client.get_audiocaptureclient()?;
        client.start_stream()?;
        Ok(Self {
            capture,
            client,
            ring: VecDeque::from(vec![0.; 2048]),
            _com: com,
        })
    }
    pub fn samples(&mut self) -> Result<Vec<f64>> {
        let mut captured = false;
        while self.capture.get_next_packet_size()?.unwrap_or(0) > 0 {
            let mut data = VecDeque::new();
            self.capture.read_from_device_to_deque(&mut data)?;
            let data: Vec<_> = data.into_iter().collect();
            for frame in data.as_chunks::<8>().0 {
                let a = f32::from_le_bytes(frame[..4].try_into()?);
                let b = f32::from_le_bytes(frame[4..].try_into()?);
                self.ring.pop_front();
                self.ring.push_back(if a.is_finite() && b.is_finite() {
                    (a as f64 + b as f64) / 2.
                } else {
                    0.
                });
                captured = true;
            }
        }
        // A stopped output endpoint may produce no packets at all. Do not replay its last FFT forever.
        if !captured {
            for _ in 0..1600 {
                self.ring.pop_front();
                self.ring.push_back(0.);
            }
        }
        Ok(self.ring.iter().copied().collect())
    }
}
impl Drop for Audio {
    fn drop(&mut self) {
        let _ = self.client.stop_stream();
    }
}
