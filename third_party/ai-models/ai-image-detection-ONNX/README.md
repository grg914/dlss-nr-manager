---
license: apache-2.0
base_model:
- capcheck/ai-image-detection
tags:
- image-classification
- vision
- ai-detection
- deepfake-detection
- vit
datasets:
- CIFAKE
metrics:
- accuracy
- f1
pipeline_tag: image-classification
library_name: transformers.js
---



# ai-image-detection (ONNX)


This is an ONNX version of [capcheck/ai-image-detection](https://huggingface.co/capcheck/ai-image-detection). It was automatically converted and uploaded using [this Hugging Face Space](https://huggingface.co/spaces/onnx-community/convert-to-onnx).


## Usage with Transformers.js


See the pipeline documentation for `image-classification`: https://huggingface.co/docs/transformers.js/api/pipelines#module_pipelines.ImageClassificationPipeline


---


# CapCheck AI Image Detection

Vision Transformer (ViT) fine-tuned for detecting AI-generated images.

## Model Lineage & Attribution

This model builds on the work of others:

| Layer | Model | Author | License |
|-------|-------|--------|---------|
| Base Architecture | [google/vit-base-patch16-224-in21k](https://huggingface.co/google/vit-base-patch16-224-in21k) | Google | Apache 2.0 |
| AI Detection Fine-tune | [dima806/ai_vs_real_image_detection](https://huggingface.co/dima806/ai_vs_real_image_detection) | dima806 | Apache 2.0 |
| This Model | capcheck/ai-image-detection | CapCheck | Apache 2.0 |

**Special thanks to:**
- **Google** for the Vision Transformer (ViT) architecture
- **dima806** for fine-tuning on the CIFAKE dataset for AI image detection

## Model Description

- **Architecture**: ViT-Base (86M parameters)
- **Input Size**: 224x224 pixels
- **Training Data**: CIFAKE dataset (AI-generated vs real images)
- **Task**: Binary classification (Real vs Fake/AI-generated)

## Usage

```python
from transformers import pipeline

detector = pipeline("image-classification", model="capcheck/ai-image-detection")
result = detector("path/to/image.jpg")

# Output:
# [{"label": "Fake", "score": 0.95}, {"label": "Real", "score": 0.05}]
```

## Labels

| Label | Description |
|-------|-------------|
| `Real` | Authentic photograph or real-world image |
| `Fake` | AI-generated or synthetically created image |

## Performance

This model was trained on the CIFAKE dataset. Performance on modern AI generators
(Flux, Midjourney v6, DALL-E 3, Stable Diffusion 3) may vary.

See [dima806's model card](https://huggingface.co/dima806/ai_vs_real_image_detection)
for detailed training metrics.

## Limitations

- Trained primarily on older AI generators (pre-2024)
- May have reduced accuracy on:
  - Very new AI generators not in training data
  - Heavily compressed images (low JPEG quality)
  - Images smaller than 224x224 pixels
- Works best on images with clear subjects

## Intended Use

- Content moderation and authenticity verification
- Research into AI-generated content detection
- Educational purposes

**Not intended for**:
- Making consequential decisions without human review
- Law enforcement evidence without corroborating sources

## Ethical Considerations

- This tool is not 100% accurate - false positives harm legitimate creators
- False negatives can allow misinformation to spread
- Use in conjunction with other verification methods
- Human review is recommended for high-stakes decisions

## Roadmap

### Current Version (v1.0.0)

Base model from dima806's CIFAKE-trained ViT. Solid foundation for AI detection.

### Planned Improvements

**Phase 1: Modern Generator Training**
- Fine-tune on images from Flux, Midjourney v6, DALL-E 3, Stable Diffusion 3
- Target: Reduce false negatives on 2024+ AI generators

**Phase 2: False Positive Reduction**
- Curate dataset of real images commonly flagged as AI
- Photography edge cases: HDR, heavy editing, digital art
- Target: <5% false positive rate

**Phase 3: Continuous Updates**
- Quarterly re-training as new generators emerge
- Community feedback integration
- Benchmark against latest AI generators

### Contributing

We welcome:
- Dataset contributions (properly licensed images)
- Bug reports and false positive/negative examples
- Benchmark results on new generators

Join the discussion: https://huggingface.co/capcheck/ai-image-detection/discussions

## License

Apache 2.0 (inherited from Google ViT and dima806's fine-tuned model)

## Citation

If you use this model, please cite:

```bibtex
@misc{capcheck-ai-detection,
  author = {CapCheck},
  title = {AI Image Detection Model},
  year = {2024},
  publisher = {HuggingFace},
  url = {https://huggingface.co/capcheck/ai-image-detection},
  note = {Based on dima806/ai_vs_real_image_detection, fine-tuned from google/vit-base-patch16-224-in21k}
}
```

## Changelog

### v1.0.0 (Initial Release)

- Published base model from dima806/ai_vs_real_image_detection
- Added proper attribution and documentation
- Established as CapCheck's source of truth for AI image detection
