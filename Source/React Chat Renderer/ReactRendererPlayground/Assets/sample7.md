# Hello World in Top 10 Languages (2022)

## C

```c
#include <stdio.h>
int main() {
   printf("Hello, World!");
   return 0;
}
```

## Java

```java
public class HelloWorld {
    public static void main(String[] args) {
        System.out.println("Hello, World!");
    }
}
```

## Python

```python
print("Hello, World!")
```

## C++

```cpp
#include <iostream>
int main() {
    std::cout << "Hello, World!";
    return 0;
}
```

## C#

```csharp
using System;
namespace HelloWorld {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("Hello, World!");
        }
    }
}
```

## Visual Basic

```vb
Module HelloWorld
    Sub Main()
        Console.WriteLine("Hello, World!")
    End Sub
End Module
```

## JavaScript

```javascript
console.log("Hello, World!");
```

## PHP

```php
<?php
echo "Hello, World!";
?>
```

## SQL

```sql
-- Not a traditional programming language, but for demonstration:
SELECT 'Hello, World!' AS Greeting;
```

## Assembly Language (x86 syntax)

```assembly
section .data
    hello db 'Hello, World!',0

section .text
    global _start

_start:
    ; write hello to stdout
    mov eax, 4
    mov ebx, 1
    mov ecx, hello
    mov edx, 13
    int 0x80

    ; exit
    mov eax, 1
    int 0x80
```

