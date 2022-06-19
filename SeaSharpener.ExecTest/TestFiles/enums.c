#include <stdlib.h>

typedef enum EnumDef_
{
	VALUE_ONE = 5,
	VALUE_TWO = 0 << 2, // todo: BinaryOperation is unsupported
	VALUE_THREE = 2 + 5, // todo: BinaryOperation is unsupported
	VALUE_FOUR = 2 - 5, // todo: BinaryOperation is unsupported
	VALUE_FIVE = 2 * 5, // todo: BinaryOperation is unsupported
} EnumDef;

enum {
	ANON_VALUE_ONE,
	ANON_VALUE_TWO = 10l, // todo: IntegralCast is unsupported
	ANON_VALUE_THREE = 0x50
};

typedef EnumDef* EnumDefPtr;

typedef struct NestedEnumStruct_
{
	//enum nestedNamesStruct
	//{
	//	One,
	//	Two
	//} nestedEnumField;

	//enum
	//{
	//	Three,
	//	Four
	//} namelessNestedEnumField;

	EnumDefPtr enumDefPtrField;
	EnumDef enumDefField;

	//enum nestedNamesStruct testField;
	int field;
} NestedEnumStruct;

void testFuncTwo(NestedEnumStruct* pObj)
{
	EnumDefPtr newEnumPtr = (EnumDefPtr) malloc(sizeof(EnumDefPtr));
	pObj->enumDefPtrField = newEnumPtr;
}