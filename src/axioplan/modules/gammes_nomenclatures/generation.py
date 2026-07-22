from dataclasses import dataclass
from itertools import product


@dataclass(frozen=True)
class GenerationVariant:
    article_code: str
    size_code: str
    color_code: str


def build_size_color_variants(base_article_code: str, sizes: list[str], colors: list[str]) -> list[GenerationVariant]:
    return [
        GenerationVariant(
            article_code=f"{base_article_code}-{size}-{color}",
            size_code=size,
            color_code=color,
        )
        for size, color in product(sizes, colors)
    ]


def calculate_net_quantity(base_quantity: float, size_coefficient: float = 1.0, color_coefficient: float = 1.0) -> float:
    return base_quantity * size_coefficient * color_coefficient


def calculate_gross_quantity(net_quantity: float, loss_rate: float = 0.0) -> float:
    if loss_rate >= 1:
        raise ValueError("loss_rate must be lower than 1")
    return net_quantity / (1 - loss_rate)


def calculate_operation_time(base_time: float, size_coefficient: float = 1.0, color_coefficient: float = 1.0) -> float:
    return base_time * size_coefficient * color_coefficient


def profile_can_generate(profile_status: str) -> bool:
    return profile_status == "VALIDATED"
