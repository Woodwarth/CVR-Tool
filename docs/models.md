# Target CVR DSP models

The target catalog has twelve models. The last model digit is
the channel count. Functions share one implementation for two/four channels;
power ratings, voltage limits, firmware profiles and physical compatibility
are read from actual device data rather than inferred from the model number.

| Model | Channels |
| --- | --- |
| DSP-654 | 4 |
| DSP-802 | 2 |
| DSP-1002 | 2 |
| DSP-1004 | 4 |
| DSP-1502 | 2 |
| DSP-2002 | 2 |
| DSP-1504 | 4 |
| DSP-2004 | 4 |
| DSP-3002 | 2 |
| DSP-3004 | 4 |
| DSP-3302 | 2 |
| DSP-4302 | 2 |

The catalog is a support target, not a declaration of tested hardware. The
three existing demo devices remain unchanged. Selected physical checks cover
DSP-1004/106119 only; native samples also cover DSP-1502. Each additional
model/firmware requires its own connection and physical verification.
