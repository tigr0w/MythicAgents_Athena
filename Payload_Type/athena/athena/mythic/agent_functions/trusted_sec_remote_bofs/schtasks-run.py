from mythic_container.MythicCommandBase import *
from mythic_container.MythicRPC import *
from ..athena_utils.mythicrpc_utilities import *
from ..athena_utils.bof_utilities import *
import json

class SchTasksRunArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="taskname",
                type=ParameterType.String,
                description="The scheduled task name to start.",
                default_value="",
                parameter_group_info=[
                    ParameterGroupInfo(
                        ui_position=0,
                        required=True,
                        )
                    ],
            ),
            CommandParameter(
                name="hostname",
                type=ParameterType.String,
                description="Optional. The target system (local system if not specified)",
                parameter_group_info=[
                    ParameterGroupInfo(
                        ui_position=1,
                        required=False,
                        )
                    ],
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            if self.command_line[0] == "{":
                self.load_args_from_json_string(self.command_line)
        else:
            raise ValueError("Missing arguments")
    
    async def parse_dictionary(self, dictionary):
        self.load_args_from_dictionary(dictionary)


class SchTasksRunCommand(CoffCommandBase):
    cmd = "schtasks-run"
    needs_admin = False
    help_cmd = """
Summary: This command runs a scheduled task.
Usage:  schtasks-run -hostname GAIA-DC -taskname \\Microsoft\\Windows\\MUI\\LpRemove
        hostname  Optional. The target system (local system if not specified)
        taskname  Required. The scheduled task name.
Note:    The full path including the task name must be given, e.g.:
            schtasks-run \\Microsoft\\Windows\\MUI\\LpRemove
            schtasks-run \\Microsoft\\windows\\MUI\\totallyreal

Credit: The TrustedSec team for the original BOF. - https://github.com/trustedsec/CS-Remote-OPs-BOF
    """
    description = "This command runs a scheduled task."
    version = 1
    script_only = True
    supported_ui_features = ["T1053.005"]
    author = "@TrustedSec"
    argument_class = SchTasksRunArguments
    attackmapping = []
    attributes = CommandAttributes(
        supported_os=[SupportedOS.Windows],
        builtin=False,
        load_only=True
    )

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        response = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
        )

        arch = taskData.Callback.Architecture

        # Ensure architecture compatibility
        if arch != "x64":
            raise Exception("BOF's are currently only supported on x64 architectures.")
        
        encoded_args = base64.b64encode(
            SerializeArgs([
            generateWString(taskData.args.get_arg("hostname") or ""),  # Use empty string if no hostname is provided
            generateWString(taskData.args.get_arg("taskname")),
            ])
        ).decode()

        file_id = await compile_and_upload_bof_to_mythic(
            taskData.Task.ID,
            "trusted_sec_remote_bofs/schtasksrun",
            f"schtasksrun.{arch}.o"
        )
        
        subtask = await SendMythicRPCTaskCreateSubtask(MythicRPCTaskCreateSubtaskMessage(
            taskData.Task.ID, 
            CommandName="coff",
            SubtaskCallbackFunction="coff_completion_callback",
            Params=json.dumps({
                "coffFile": file_id,
                "functionName": "go",
                "arguments": encoded_args,
                "timeout": "60",
            }),
            Token=taskData.Task.TokenID,
        ))

        # We did it!
        return response

    async def process_response(self, response: AgentResponse):
        response.response["output"] = response.response["output"].replace("\\r\\n", "\r\n")
        pass
