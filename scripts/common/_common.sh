#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

COMMONSOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do # resolve $SOURCE until the file is no longer a symlink
  COMMONDIR="$( cd -P "$( dirname "$COMMONSOURCE" )" && pwd )"
  COMMONSOURCE="$(readlink "$COMMONSOURCE")"
  [[ $COMMONSOURCE != /* ]] && COMMONSOURCE="$COMMONDIR/$COMMONSOURCE" # if $SOURCE was a relative symlink, we need to resolve it relative to the path where the symlink file was located
done
COMMONDIR="$( cd -P "$( dirname "$COMMONSOURCE" )" && pwd )"

source "$COMMONDIR/_prettyprint.sh"
source "$COMMONDIR/_rid.sh"

# TODO: Replace this with a dotnet generation
export TFM=dnxcore50
export REPOROOT=$(cd $COMMONDIR/../.. && pwd)
export OUTPUT_ROOT=$REPOROOT/artifacts/$RID
export DNX_DIR=$OUTPUT_ROOT/dnx
export DNX_ROOT=$DNX_DIR/bin
export STAGE1_DIR=$OUTPUT_ROOT/stage1
export STAGE2_DIR=$OUTPUT_ROOT/stage2
export HOST_DIR=$OUTPUT_ROOT/corehost

[ -z "$DOTNET_INSTALL_DIR" ] && export DOTNET_INSTALL_DIR=$REPOROOT/.dotnet_stage0/$RID
[ -z "$DOTNET_CLI_VERSION" ] && export DOTNET_CLI_VERSION=0.1.0.0
[ -z "$DOTNET_HOME" ] && export DOTNET_HOME=$STAGE2_DIR && export PATH=$STAGE2_DIR/bin:$PATH
[ -z "$CONFIGURATION" ] && export CONFIGURATION=Debug
[ -z "$NOCACHE" ] && export NOCACHE=""

unset COMMONSOURCE
unset COMMONDIR
