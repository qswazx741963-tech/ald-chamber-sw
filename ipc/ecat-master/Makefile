# ALD EtherCAT 主站程式（IPC，Linux）
#   make                     只編模擬版（不需要 IgH，可在任何 Linux 編譯）
#   make IGH=/opt/etherlab   編正式版（IgH EtherLAB 安裝路徑）
#   make test                單元測試（gcc + AddressSanitizer）
CC      ?= gcc
CFLAGS  ?= -std=c11 -O2 -g -Wall -Wextra -Werror
INC      = -Isrc -I../../firmware/common/registers
SRC      = src/main.c src/bridge.c src/ecat_sim.c
LIBS     = -lpthread

ifdef IGH
CFLAGS  += -DHAVE_IGH -I$(IGH)/include
SRC     += src/ecat_igh.c
LIBS    += -L$(IGH)/lib -lethercat
endif

all: ald-ecat

ald-ecat: $(SRC) $(wildcard src/*.h ../../firmware/common/registers/*.h)
	$(CC) $(CFLAGS) $(INC) -o $@ $(SRC) $(LIBS)

test: tests/test_bridge.c src/bridge.c src/bridge.h
	$(CC) -std=c11 -O1 -g -Wall -Wextra -Werror -fsanitize=address,undefined $(INC) -o tests/test_bridge tests/test_bridge.c src/bridge.c src/ecat_sim.c
	./tests/test_bridge

clean:
	rm -f ald-ecat tests/test_bridge

.PHONY: all test clean
