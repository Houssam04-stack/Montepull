import unittest
from pathlib import Path
import sys


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from axioplan.modules.gammes_nomenclatures.generation import (
    build_size_color_variants,
    calculate_gross_quantity,
    calculate_net_quantity,
    calculate_operation_time,
    profile_can_generate,
)


class GenerationTests(unittest.TestCase):
    def test_build_size_color_variants(self):
        variants = build_size_color_variants("PULL", ["S", "M"], ["MINT", "NOIR"])

        self.assertEqual(
            [variant.article_code for variant in variants],
            [
                "PULL-S-MINT",
                "PULL-S-NOIR",
                "PULL-M-MINT",
                "PULL-M-NOIR",
            ],
        )

    def test_quantity_calculation(self):
        net = calculate_net_quantity(0.5, size_coefficient=1.10, color_coefficient=1.02)
        gross = calculate_gross_quantity(net, loss_rate=0.05)

        self.assertEqual(round(net, 3), 0.561)
        self.assertEqual(round(gross, 3), 0.591)

    def test_operation_time_calculation(self):
        self.assertEqual(calculate_operation_time(18, size_coefficient=1.10, color_coefficient=1.0), 19.8)

    def test_only_validated_profile_can_generate(self):
        self.assertTrue(profile_can_generate("VALIDATED"))
        self.assertFalse(profile_can_generate("DRAFT"))
        self.assertFalse(profile_can_generate("TO_VALIDATE"))
        self.assertFalse(profile_can_generate("BLOCKED"))
        self.assertFalse(profile_can_generate("OBSOLETE"))


if __name__ == "__main__":
    unittest.main()
