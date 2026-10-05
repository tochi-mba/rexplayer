# Specifications

rexplayer's format code is written from published specifications only (ADR-0016). This page says
which edition each implementation follows, so a future change can be checked against the same
text. Every source file in a format folder names its clauses in its `// Spec:` header.

| Area | Code | Followed |
|---|---|---|
| WAV, RF64, BW64 | `Containers/Riff` | Microsoft Multimedia Programming Interface and Data Specifications 1.0; EBU Tech 3306; ITU-R BS.2088 |
| AIFF, AIFF-C | `Containers/Riff` | Apple Audio Interchange File Format 1.3 and the AIFF-C draft |
| PCM, G.711 | `Codecs.Software/Pcm` | Microsoft WAVE sample layouts; ITU-T G.711 |
| FLAC | `Codecs/Flac`, `Codecs.Software/Flac`, `Containers/Flac` | IETF RFC 9639 (2024) |
| Vorbis comments | `Containers/Tags` | Xiph.Org Vorbis I specification, section 5 |
| ID3 | `Containers/Tags` | ID3 tag versions 2.2.0, 2.3.0 and 2.4.0 informal standards; ID3v2 Chapter Frame Addendum 1.0; ID3v1 |
| APE tag footer | `Containers/Mpeg` | APE tag specification 2.0 (footer only: the tag is skipped) |
| MPEG audio frames | `Codecs/Mpeg`, `Containers/Mpeg` | ISO/IEC 11172-3 (1993) and ISO/IEC 13818-3 frame headers; the MPEG 2.5 extension for 8 to 12 kHz |
| Layer III decoding | `Codecs.Software/Mpeg` | ISO/IEC 11172-3 clause 2.4.3.4 and ISO/IEC 13818-3 clause 2.4.3.4, as presented in "MPEG-Layer3 Bitstream Syntax and Decoding" issue 1.2 (Sieler and Sperschneider, 1997) |
| Layer III tables | `Codecs.Software/Mpeg/Layer3Tables.cs` | Transcribed from the tables of the same document (the standard's Annex B tables 3-B.3, 3-B.6, 3-B.7, 3-B.8, 3-B.9 and the lower-rate band tables). Tests prove each Huffman table is a complete prefix code and the synthesis window mirrors around its centre |
| Encoder tags | `Codecs/Mpeg/MpegInfoTag.cs` | Xing/Info VBR header; LAME tag revision 1; Fraunhofer VBRI header |
| Gapless MP3 | `Containers/Mpeg` | The LAME tag's encoder delay and padding plus the standard decoder delay of 529 samples |

Conformance evidence lives in the tests: independent-encoder fixtures for each format
(`docs/fixtures.md`) and, for MP3, the "full accuracy" criterion of ISO/IEC 11172-4.
