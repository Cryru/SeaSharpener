#include <stdlib.h>
#include <stdio.h>

typedef void (*voidWithIntArg)(int);

typedef struct StructWithFunction_
{
	voidWithIntArg test;
} StructWithFunction;

void actualFunction(int arg)
{
	printf("%d", arg);
}

int main()
{
	// Illegal in conversion due to StructWithFunction_ becoming a class V
	// StructWithFunction* variable = (StructWithFunction*) malloc(sizeof(StructWithFunction));

	StructWithFunction variable;
	variable.test = actualFunction;

	return 0;
}