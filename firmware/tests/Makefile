# 在 PC 上跑韌體邏輯的單元測試（不需要板子）
#   make -C firmware/tests        編譯並執行
#   make -C firmware/tests clean
CC      ?= gcc
CFLAGS  ?= -std=c99 -O1 -g -Wall -Wextra -Werror -Wno-unused-parameter -fsanitize=address,undefined
INC      = -I../common -I../common/registers -I../main_board/app -I../exec_board/app -I.
COMMON   = ../common/bp_proto.c fake_hal.c
MAIN_SRC = $(wildcard ../main_board/app/*.c)
EXEC_SRC = $(wildcard ../exec_board/app/*.c)

all: test_main_board test_exec_board
	./test_main_board
	./test_exec_board

test_main_board: test_main_board.c $(MAIN_SRC) $(COMMON) $(wildcard ../*/app/*.h ../common/*.h ../common/registers/*.h)
	$(CC) $(CFLAGS) $(INC) -o $@ test_main_board.c $(MAIN_SRC) $(COMMON) -lm

test_exec_board: test_exec_board.c $(EXEC_SRC) $(COMMON) $(wildcard ../*/app/*.h ../common/*.h ../common/registers/*.h)
	$(CC) $(CFLAGS) $(INC) -o $@ test_exec_board.c $(EXEC_SRC) $(COMMON) -lm

clean:
	rm -f test_main_board test_exec_board

.PHONY: all clean
