### Local Inference Setup (Intel GPU Optimized via IPEX-LLM — Recommended for Windows!)
This example uses a portable Ollama build with **IPEX-LLM** integration for fast, stable local inference on Intel GPUs (Arc / Xe Graphics).

**Why this build?**
- Standard Ollama can lag (> 60s responses on 32B models) or reboot frequently without full optimization.
- IPEX-LLM version: Smooth, high-token-rate runs on Intel hardware — no NVIDIA needed!

**Tested on**: Windows with Intel Arc + latest drivers.

**Quick Setup Steps** (Portable — No Full Install Needed):
1. **Update Intel Drivers** (essential for stability!):  
   Download latest from [intel.com/support](https://www.intel.com/content/www/us/en/support/products/80939/graphics.html) (search your GPU model).

2. **Download IPEX-LLM Ollama Portable Zip**:  
   Get the latest Windows version from Intel's guide:  
   [GitHub Quickstart - Ollama Portable Zip](https://github.com/intel/ipex-llm/blob/main/docs/mddocs/Quickstart/ollama_portable_zip_quickstart.md)  
   (Your ollama-ipex-llm-2.2.0-win is solid; upgrade if needed for newer features.)
   [Or here](https://github.com/ipex-llm/ipex-llm/releases/tag/v2.2.0)

4. **Unzip & Start**:  
   - Extract the zip to a folder.  
   - Run `start-ollama.bat` (or equivalent in latest zip).  

5. **Pull a Model**:  
   In a command prompt:  
   `ollama pull qwen2.5:32b-instruct-q5_K_M`  
   (Quantized GGUF models recommended for max speed.)

6. **Run the Server**:  
   Ollama serves automatically on startup — accessible at `http://localhost:11434`.

NVIDIA/Standard Ollama users: Use official Ollama download + CUDA for equivalent performance.

Feedback welcome if issues on your hardware!
