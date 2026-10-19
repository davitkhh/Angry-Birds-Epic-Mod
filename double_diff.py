#!/usr/bin/env python3
"""Double the difference of two numbers and add it to the first: a + 2 * (a - b)."""
import sys


def double_diff(a: float, b: float) -> float:
    return a + 2 * (a - b)


def main() -> None:
    if len(sys.argv) == 3:
        a, b = float(sys.argv[1]), float(sys.argv[2])
    else:
        a = float(input("First number: "))
        b = float(input("Second number: "))
    result = double_diff(a, b)
    print(int(result) if result.is_integer() else result)


if __name__ == "__main__":
    main()
