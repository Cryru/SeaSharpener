#include <header.h>
#include <includeFolder/headerInFolder.h>
#include <stdio.h>

int main(int argc, char* argv[])
{
	#ifdef HEADER_ONE
	printf("Hello Header One!");
	#endif

	functionInHeaderTwo(1, 2);

	return 0;
}