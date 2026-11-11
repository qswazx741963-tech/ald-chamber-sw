# ALD 整機模擬器（虛擬機台）
#   make -C sim          編出 build/libaldvm.a，並跑整合測試
#
# 做法：主控板與執行板的「正式韌體原始碼」照常編譯，跟 sim_hal.c（-DSIM_NODE=n）一起 ld -r 成
# 一個物件，再用 objcopy 把該物件「自己定義的全域符號」全部加上 nN_ 前綴（n0 = 主控板、n1~n4 = 槽 1~4）。
# 這樣 5 份韌體的 static 變數、g_ir / g_hr 各自獨立，可以放在同一個程式裡跑；韌體原始碼完全不用改。
CC      ?= gcc
LD      ?= ld
OBJCOPY ?= objcopy
NM      ?= nm
FW       = ../firmware
CFLAGS  ?= -std=c11 -O2 -g -Wall -Wextra -Werror -Wno-unused-parameter
CFLAGS  += -fno-common
INC      = -I. -I$(FW)/common -I$(FW)/common/registers -I$(FW)/main_board/app -I$(FW)/exec_board/app

COMMON_SRC = $(FW)/common/bp_proto.c $(FW)/common/gauge.c
MAIN_SRC   = $(wildcard $(FW)/main_board/app/*.c) $(COMMON_SRC)
EXEC_SRC   = $(wildcard $(FW)/exec_board/app/*.c) $(COMMON_SRC)
MAIN_OBJ   = $(addprefix build/main/,$(notdir $(MAIN_SRC:.c=.o)))
EXEC_OBJ   = $(addprefix build/exec/,$(notdir $(EXEC_SRC:.c=.o)))
VM_OBJ     = build/vm.o build/plant.o build/slk_emu.o
HDRS       = $(wildcard *.h $(FW)/common/*.h $(FW)/common/registers/*.h $(FW)/*/app/*.h)

vpath %.c $(FW)/common $(FW)/main_board/app $(FW)/exec_board/app

all: build/libaldvm.a test

build/main/%.o: %.c $(HDRS)
	@mkdir -p build/main
	$(CC) $(CFLAGS) $(INC) -c $< -o $@

build/exec/%.o: %.c $(HDRS)
	@mkdir -p build/exec
	$(CC) $(CFLAGS) $(INC) -c $< -o $@

build/hal%.o: sim_hal.c $(HDRS)
	@mkdir -p build
	$(CC) $(CFLAGS) $(INC) -DSIM_NODE=$* -c $< -o $@

build/vm.o build/plant.o build/slk_emu.o: build/%.o: %.c $(HDRS)
	@mkdir -p build
	$(CC) $(CFLAGS) $(INC) -c $< -o $@

# 合併 + 加前綴
build/n0.o: $(MAIN_OBJ) build/hal0.o
	$(LD) -r -o build/n0.raw.o $^
	$(NM) -g --defined-only build/n0.raw.o | awk '{print $$3" n0_"$$3}' > build/n0.syms
	$(OBJCOPY) --redefine-syms=build/n0.syms build/n0.raw.o $@

build/n%.o: $(EXEC_OBJ) build/hal%.o
	$(LD) -r -o build/n$*.raw.o $^
	$(NM) -g --defined-only build/n$*.raw.o | awk '{print $$3" n$*_"$$3}' > build/n$*.syms
	$(OBJCOPY) --redefine-syms=build/n$*.syms build/n$*.raw.o $@

build/libaldvm.a: build/n0.o build/n1.o build/n2.o build/n3.o build/n4.o $(VM_OBJ)
	ar rcs $@ $^

build/test_vm: tests/test_vm.c ../ipc/ecat-master/src/bridge.c build/libaldvm.a
	$(CC) $(CFLAGS) $(INC) -I../ipc/ecat-master/src -o $@ tests/test_vm.c ../ipc/ecat-master/src/bridge.c build/libaldvm.a -lm

test: build/test_vm
	./build/test_vm

clean:
	rm -rf build

.PHONY: all test clean
