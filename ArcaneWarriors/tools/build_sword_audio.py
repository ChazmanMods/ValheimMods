from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfiltfilt, welch


ROOT = Path(r"E:\Valheim Mods\ArcaneWarriors")
SOURCE = ROOT / "sounds" / "source_lightning"
OUTPUT = ROOT / "sounds" / "generated"
SAMPLE_RATE = 48000
RNG = np.random.default_rng(7319)


def read_source(path: Path) -> np.ndarray:
    rate, audio = wavfile.read(path)
    if rate != SAMPLE_RATE:
        raise ValueError(f"Unexpected sample rate in {path}: {rate}")
    audio = audio.astype(np.float64) / 32768.0
    if audio.ndim == 1:
        audio = np.column_stack([audio, audio])
    return audio[:, :2]


def filter_audio(audio: np.ndarray, low: float, high: float) -> np.ndarray:
    sos = butter(4, [low, high], btype="bandpass", fs=SAMPLE_RATE, output="sos")
    return sosfiltfilt(sos, audio, axis=0)


def normalize(audio: np.ndarray, peak: float = 0.92) -> np.ndarray:
    maximum = np.max(np.abs(audio))
    if maximum > 0:
        audio = audio * (peak / maximum)
    return np.clip(audio, -1.0, 1.0)


def write_audio(name: str, audio: np.ndarray) -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    wavfile.write(OUTPUT / name, SAMPLE_RATE, (normalize(audio) * 32767.0).astype(np.int16))


def make_periodic_hum(sources: list[np.ndarray], seconds: float = 5.0) -> np.ndarray:
    tails = []
    for source in sources:
        start = int(0.14 * SAMPLE_RATE)
        tails.append(np.mean(source[start:], axis=1))
    palette = np.concatenate(tails)
    palette = filter_audio(palette[:, None], 55.0, 2600.0)[:, 0]

    frequencies, power = welch(palette, fs=SAMPLE_RATE, nperseg=8192, noverlap=6144)
    count = int(seconds * SAMPLE_RATE)
    target_frequencies = np.fft.rfftfreq(count, 1.0 / SAMPLE_RATE)
    magnitude = np.sqrt(np.interp(target_frequencies, frequencies, power, left=0.0, right=0.0))
    magnitude *= 1.0 / np.sqrt(np.maximum(target_frequencies, 45.0))
    magnitude[target_frequencies > 2800.0] = 0.0
    magnitude[target_frequencies < 48.0] = 0.0

    channels = []
    for channel in range(2):
        phase = RNG.uniform(0.0, 2.0 * np.pi, magnitude.size)
        spectrum = magnitude * np.exp(1j * phase)
        spectrum[0] = 0.0
        bed = np.fft.irfft(spectrum, n=count)
        channels.append(bed)
    hum = np.column_stack(channels)

    dominant = frequencies[(frequencies >= 70.0) & (frequencies <= 700.0)]
    dominant_power = power[(frequencies >= 70.0) & (frequencies <= 700.0)]
    peak_frequency = dominant[np.argmax(dominant_power)] if dominant.size else 118.0
    time = np.arange(count) / SAMPLE_RATE
    tone = (
        np.sin(2.0 * np.pi * peak_frequency * time)
        + 0.38 * np.sin(2.0 * np.pi * peak_frequency * 2.01 * time + 0.7)
        + 0.18 * np.sin(2.0 * np.pi * peak_frequency * 3.02 * time + 1.4)
    )
    hum += tone[:, None] * np.std(hum) * 0.48
    return normalize(np.tanh(hum * 2.4), 0.72)


def variable_speed(audio: np.ndarray, seconds: float, start_step: float, end_step: float) -> np.ndarray:
    output_count = int(seconds * SAMPLE_RATE)
    steps = np.linspace(start_step, end_step, output_count)
    positions = np.cumsum(steps)
    positions -= positions[0]
    positions *= (len(audio) - 2) / max(positions[-1], 1.0)
    source_positions = np.arange(len(audio))
    return np.column_stack([
        np.interp(positions, source_positions, audio[:, channel]) for channel in range(2)
    ])


def make_deploy(hum: np.ndarray, impact: np.ndarray) -> np.ndarray:
    duration = 0.82
    count = int(duration * SAMPLE_RATE)
    reverse_tail = impact[int(0.10 * SAMPLE_RATE):int(0.72 * SAMPLE_RATE)][::-1]
    swell = variable_speed(reverse_tail, duration, 0.55, 1.65)
    swell = filter_audio(swell, 90.0, 5200.0)
    envelope = np.sin(np.linspace(0.0, np.pi * 0.5, count)) ** 1.7
    result = swell * envelope[:, None] * 0.78
    hum_part = hum[:count] * envelope[:, None]
    result += hum_part * 0.42
    transient = impact[:int(0.22 * SAMPLE_RATE)]
    offset = int(0.60 * SAMPLE_RATE)
    available = min(len(transient), count - offset)
    result[offset:offset + available] += transient[:available] * np.linspace(0.75, 0.0, available)[:, None] * 0.48
    return normalize(result)


def make_retract(hum: np.ndarray, impact: np.ndarray) -> np.ndarray:
    duration = 0.72
    count = int(duration * SAMPLE_RATE)
    body = variable_speed(hum[:int(1.1 * SAMPLE_RATE)], duration, 0.95, 0.38)
    body = filter_audio(body, 55.0, 2400.0)
    envelope = np.cos(np.linspace(0.0, np.pi * 0.5, count)) ** 1.5
    body *= envelope[:, None]
    spark = impact[int(0.08 * SAMPLE_RATE):int(0.42 * SAMPLE_RATE)][::-1]
    spark = variable_speed(spark, 0.34, 1.1, 0.55)
    available = min(len(spark), count)
    body[:available] += spark[:available] * np.linspace(0.45, 0.0, available)[:, None]
    return normalize(body)


def make_swing(source: np.ndarray, index: int) -> np.ndarray:
    duration = 0.50 + index * 0.025
    count = int(duration * SAMPLE_RATE)
    tail = source[int(0.04 * SAMPLE_RATE):]
    swept = variable_speed(tail, duration, 0.42 + index * 0.05, 1.8 + index * 0.12)
    swept = filter_audio(swept, 180.0, 6200.0)
    envelope = np.sin(np.linspace(0.0, np.pi, count)) ** 1.35
    swept *= envelope[:, None]
    pan = np.linspace(-0.30, 0.30, count) * (-1.0 if index % 2 else 1.0)
    swept[:, 0] *= 1.0 - pan
    swept[:, 1] *= 1.0 + pan
    return normalize(np.tanh(swept * 1.8), 0.88)


def main() -> None:
    sources = [read_source(SOURCE / f"lightning_impact_{number:02d}.wav") for number in range(1, 6)]
    hum = make_periodic_hum(sources)
    write_audio("Sword_Ancients_Idle_Hum.wav", hum)
    write_audio("Sword_Ancients_Deploy.wav", make_deploy(hum, sources[2]))
    write_audio("Sword_Ancients_Retract.wav", make_retract(hum, sources[4]))
    for index, source in enumerate(sources[:3], start=1):
        write_audio(f"Sword_Ancients_Swing_{index:02d}.wav", make_swing(source, index))


if __name__ == "__main__":
    main()
