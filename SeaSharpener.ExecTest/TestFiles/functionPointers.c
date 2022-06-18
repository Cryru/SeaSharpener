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
	StructWithFunction* variable = (StructWithFunction*) malloc(sizeof(StructWithFunction));
	variable->test = actualFunction;
}