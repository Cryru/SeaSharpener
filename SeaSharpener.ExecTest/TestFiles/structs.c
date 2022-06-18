typedef union UnionStruct_
{
	long value;
	void* pointer;
} UnionStruct;

typedef struct StructWithLotsOfStuff_
{
	union
	{
		long value;
		void* pointer;
	} inlineUnionStruct;

	UnionStruct unionStructField;
} StructWithLotsOfStuff;

typedef StructWithLotsOfStuff* StructWithLotsOfStuffPtr;

typedef struct TestStruct_
{
	int internal; // reserved C# word
	void* voidPtr;
	StructWithLotsOfStuffPtr otherStruct;
} TestStruct;

typedef struct OuterStruct_
{
	struct
	{
		int value;
	};

	char randomBool;
} OuterStruct_;

typedef struct StructThatContainsAnArray_
{
	int arr[2];
	int arrTwoD[5][5];
} StructThatContainsAnArray;

void testFunc(TestStruct* pObj)
{
	pObj->internal = 5;
}